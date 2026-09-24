using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Drives <c>Views/InvitePanel</c> — the "Invite people" card the web Share
/// window leads with. Domain-agnostic: it talks to an <see cref="IInviteTarget"/>
/// so the same panel serves lists (issue #55) and documents (issue #56).
///
/// Three product rules from /help/documents and /help/api/sharing are encoded
/// here rather than left to the server's error message:
/// <list type="bullet">
/// <item>Only the <b>true owner</b> may add, re-role or remove people — not even
/// a manager-role collaborator can — so everything is gated on
/// <see cref="IsOwner"/>.</item>
/// <item>Creating an invite is <b>subscriber-only</b>, but the controls stay
/// visible and locked with an Upgrade prompt rather than disappearing, and the
/// invitee never pays.</item>
/// <item>Revoking is deliberately <b>not</b> subscriber-gated, so a lapsed owner
/// can still shut off access they granted — Revoke stays live even while the
/// send form is locked.</item>
/// </list>
/// </summary>
public partial class InvitePanelViewModel : ObservableObject
{
    private readonly SessionService _session;
    private IInviteTarget? _target;

    /// <summary>Guards against a stale in-flight load painting over a newer selection.</summary>
    private int _loadGeneration;

    public ObservableCollection<EmailInvite> Invites { get; } = new();
    public ObservableCollection<InviteOptionViewModel> Roles { get; } = new();
    public ObservableCollection<InviteOptionViewModel> Expiries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendInviteCommand))]
    private string inviteEmail = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    /// <summary>True once a list/document is selected; the whole card hides otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpgradePrompt), nameof(ShowNotOwnerNotice))]
    [NotifyCanExecuteChangedFor(nameof(SendInviteCommand))]
    private bool hasTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage), nameof(ShowUpgradePrompt), nameof(ShowNotOwnerNotice))]
    [NotifyCanExecuteChangedFor(nameof(SendInviteCommand))]
    private bool isOwner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage), nameof(ShowUpgradePrompt))]
    [NotifyCanExecuteChangedFor(nameof(SendInviteCommand))]
    private bool isSubscriber;

    /// <summary>"list" / "document" — folded into the card's prose.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrivacyNote), nameof(UpgradeNote), nameof(NotOwnerNote), nameof(EmptyNote))]
    private string resourceNoun = "item";

    /// <summary>Send is owner + subscriber; Revoke only needs owner.</summary>
    public bool CanManage => IsOwner && IsSubscriber;

    public bool ShowUpgradePrompt => HasTarget && IsOwner && !IsSubscriber;

    public bool ShowNotOwnerNotice => HasTarget && !IsOwner;

    public string PrivacyNote =>
        $"Invited people get access while this {ResourceNoun} stays private. " +
        "They don't need an account yet, and they never pay for the access you grant.";

    public string UpgradeNote =>
        $"Inviting people to a {ResourceNoun} is a subscriber feature. Upgrade on the web to send invites — " +
        "anyone you invite still gets their access for free. You can always revoke an invite you already sent.";

    public string NotOwnerNote =>
        $"Only the owner of this {ResourceNoun} can invite people or change roles.";

    public string EmptyNote => $"No invites yet. Invite someone by email to share this {ResourceNoun} privately.";

    public InvitePanelViewModel(SessionService session)
    {
        _session = session;

        Roles.Add(new InviteOptionViewModel
        {
            Label = "Read-only",
            Detail = ShareRoles.DescriptionFor(ShareRoles.Viewer),
            Role = ShareRoles.Viewer,
            IsSelected = true,
        });
        Roles.Add(new InviteOptionViewModel
        {
            Label = "Edit",
            Detail = ShareRoles.DescriptionFor(ShareRoles.Editor),
            Role = ShareRoles.Editor,
        });
        Roles.Add(new InviteOptionViewModel
        {
            Label = "Admin",
            Detail = ShareRoles.DescriptionFor(ShareRoles.Admin),
            Role = ShareRoles.Admin,
        });

        // Matches the web's expiry choices for a share grant.
        Expiries.Add(new InviteOptionViewModel { Label = "Never", Duration = null, IsSelected = true });
        Expiries.Add(new InviteOptionViewModel { Label = "7 days", Duration = TimeSpan.FromDays(7) });
        Expiries.Add(new InviteOptionViewModel { Label = "30 days", Duration = TimeSpan.FromDays(30) });

        RefreshSubscriberState();
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionService.CurrentUser))
                RefreshSubscriberState();
        };
    }

    /// <summary>
    /// Point the panel at a resource (or at nothing). Called by the hosting
    /// view whenever its selection changes.
    /// </summary>
    public async Task SetTargetAsync(IInviteTarget? target)
    {
        _target = target;
        var generation = ++_loadGeneration;

        Invites.Clear();
        ErrorMessage = null;
        StatusMessage = null;
        InviteEmail = "";
        IsOwner = false;
        HasTarget = target is not null;
        ResourceNoun = target?.ResourceNoun ?? "item";
        RefreshSubscriberState();

        if (target is null) return;

        IsLoading = true;
        try
        {
            var ownerId = await target.GetOwnerUserIdAsync();
            if (generation != _loadGeneration) return;

            // No owner id reported → don't unlock sharing on a guess; the server
            // is the real gate and answers 404 for a non-owner anyway.
            IsOwner = ownerId is { Length: > 0 }
                      && string.Equals(ownerId, _session.CurrentUser?.Id, StringComparison.Ordinal);

            if (!IsOwner) return;

            await ReloadInvitesAsync(generation);
        }
        catch (InterlinedApiException ex)
        {
            // 404 here is the documented "you are not the owner" answer — the
            // server never leaks whether the resource exists.
            if (generation == _loadGeneration && ex.StatusCode != 404)
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (generation == _loadGeneration) IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadInvitesAsync(_loadGeneration);

    private bool CanSendInvite() =>
        HasTarget && CanManage && IsValidEmail(InviteEmail);

    [RelayCommand(CanExecute = nameof(CanSendInvite))]
    private async Task SendInviteAsync()
    {
        if (_target is not { } target) return;

        var role = Roles.FirstOrDefault(r => r.IsSelected)?.Role ?? ShareRoles.Viewer;
        var duration = Expiries.FirstOrDefault(e => e.IsSelected)?.Duration;
        var expiresAt = duration is { } span ? DateTimeOffset.UtcNow.Add(span) : (DateTimeOffset?)null;
        var email = InviteEmail.Trim();

        ErrorMessage = null;
        StatusMessage = null;
        IsLoading = true;
        try
        {
            var created = await target.CreateInviteAsync(email, role, expiresAt);
            InviteEmail = "";
            StatusMessage = $"Invite emailed to {created.Email} as {created.RoleLabel}.";

            // Read-after-write: the create response carries no token, and the
            // token is what Revoke needs.
            await ReloadInvitesAsync(_loadGeneration);
        }
        catch (InterlinedApiException ex)
        {
            // 403 is the subscriber gate; it runs before any resource lookup so
            // existence never leaks to a free account.
            ErrorMessage = ex.StatusCode == 403
                ? $"{ex.Message} Inviting requires a subscription — the person you invite still gets access free."
                : ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Revoke stays available to a lapsed subscriber, by design — the guard is
    /// ownership plus a token, not a subscription. (Kept as an in-body check
    /// rather than a CanExecute so the row button can bind IsEnabled directly.)
    /// </summary>
    [RelayCommand]
    private async Task RevokeInviteAsync(EmailInvite invite)
    {
        if (!IsOwner || _target is not { } target || invite.Token is not { Length: > 0 } token) return;

        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await target.RevokeInviteAsync(token);
            StatusMessage = $"Invite to {invite.Email} revoked.";
            await ReloadInvitesAsync(_loadGeneration);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CopyInviteLink(EmailInvite invite)
    {
        if (_target is not { } target) return;

        var url = invite.Url
                  ?? (invite.Token is { Length: > 0 } token ? target.InviteUrlFor(token) : null);
        if (url is null) return;

        try
        {
            Clipboard.SetText(url);
            StatusMessage = "Invite link copied. It only works for the invited address.";
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard; never take the app down for it.
            ErrorMessage = $"Couldn't copy the invite link: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectRole(InviteOptionViewModel option) => SelectOnly(Roles, option);

    [RelayCommand]
    private void SelectExpiry(InviteOptionViewModel option) => SelectOnly(Expiries, option);

    private static void SelectOnly(
        IEnumerable<InviteOptionViewModel> options, InviteOptionViewModel chosen)
    {
        foreach (var option in options)
            option.IsSelected = ReferenceEquals(option, chosen);
    }

    private async Task ReloadInvitesAsync(int generation)
    {
        if (_target is not { } target || !IsOwner) return;

        try
        {
            var invites = await target.GetInvitesAsync();
            if (generation != _loadGeneration) return;

            Invites.Clear();
            foreach (var invite in invites)
                Invites.Add(invite);
        }
        catch (InterlinedApiException ex)
        {
            if (generation == _loadGeneration) ErrorMessage = ex.Message;
        }
    }

    private void RefreshSubscriberState()
    {
        // Authoritative per /help/api: customerStatus is "free", "subscriber",
        // "subscriber:monthly" or "subscriber:annual", and *any* non-"free"
        // value grants subscriber access. A null/absent status is treated as
        // free — the server is the real gate either way.
        var status = _session.CurrentUser?.CustomerStatus;
        IsSubscriber = status is { Length: > 0 }
                       && !string.Equals(status, "free", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // The server validates syntactically too (400 "A valid email address is
        // required"); this just keeps Send Invite disabled until it's plausible.
        return MailAddress.TryCreate(value.Trim(), out _);
    }
}
