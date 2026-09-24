using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class ConnectedAccountsViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<LinkedIdentity> Identities { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string mastodonInstance = "";

    // ── LinkedIn pages ──────────────────────────────────────────────────────
    // Syncing pages is the prerequisite for having any LinkedIn destination to
    // post to. Note an empty target list is ambiguous on its own: the endpoint
    // returns 200 {"targets":[]} whether or not LinkedIn is linked, so link
    // state is taken from /api/user/identities instead.

    public ObservableCollection<LinkedInTarget> LinkedInTargets { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkedInStatus))]
    [NotifyCanExecuteChangedFor(nameof(SyncLinkedInPagesCommand))]
    private bool isLinkedInLinked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncLinkedInPagesCommand))]
    private bool isSyncingLinkedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkedInStatus))]
    private string? linkedInMessage;

    /// <summary>
    /// A single honest line about LinkedIn, distinguishing "not connected" from
    /// "connected but no pages" — which an empty target list alone cannot.
    /// </summary>
    public string LinkedInStatus
    {
        get
        {
            if (LinkedInMessage is { Length: > 0 }) return LinkedInMessage;
            if (!IsLinkedInLinked) return "LinkedIn isn't connected. Connect it to cross-post.";
            return LinkedInTargets.Count > 0
                ? $"{LinkedInTargets.Count} LinkedIn destination(s) available."
                : "LinkedIn is connected, but no pages have been synced yet.";
        }
    }

    public ConnectedAccountsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var identities = await _session.Api.GetLinkedIdentitiesAsync();

            Identities.Clear();
            foreach (var identity in identities)
                Identities.Add(identity);

            // Link state comes from identities, never from an empty target list.
            IsLinkedInLinked = identities.Any(i =>
                i.Provider is { Length: > 0 } p
                && p.StartsWith("linkedin", StringComparison.OrdinalIgnoreCase));

            await LoadLinkedInTargetsAsync();

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ConnectBluesky() => _session.Api.OpenProviderAuthorize("bluesky");

    [RelayCommand]
    private void ConnectLinkedIn() => _session.Api.OpenProviderAuthorize("linkedin");

    [RelayCommand]
    private void ConnectTwitter() => _session.Api.OpenProviderAuthorize("twitter");

    private bool CanConnectMastodon() => !string.IsNullOrWhiteSpace(MastodonInstance);

    [RelayCommand(CanExecute = nameof(CanConnectMastodon))]
    private void ConnectMastodon() => _session.Api.OpenProviderAuthorize("mastodon", MastodonInstance.Trim());

    // Unlink's response shape isn't live-verified (see InterlinedApiClient.CrossPost.cs) —
    // treat success as "assume it worked" and just reload the list.
    [RelayCommand]
    private async Task DisconnectAsync(LinkedIdentity identity)
    {
        try
        {
            await _session.Api.RemoveIdentityAsync(identity.Provider);
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnMastodonInstanceChanged(string value) => ConnectMastodonCommand.NotifyCanExecuteChanged();
    // ── LinkedIn ────────────────────────────────────────────────────────────

    private async Task LoadLinkedInTargetsAsync()
    {
        try
        {
            var targets = await _session.Api.GetLinkedInTargetsAsync();
            LinkedInTargets.Clear();
            foreach (var target in targets)
                LinkedInTargets.Add(target);
        }
        catch (InterlinedApiException)
        {
            // Supplementary: a failure here must not blank the identity list.
            LinkedInTargets.Clear();
        }
        OnPropertyChanged(nameof(LinkedInStatus));
    }

    private bool CanSyncLinkedInPages() => IsLinkedInLinked && !IsSyncingLinkedIn;

    [RelayCommand(CanExecute = nameof(CanSyncLinkedInPages))]
    private async Task SyncLinkedInPagesAsync()
    {
        IsSyncingLinkedIn = true;
        LinkedInMessage = null;
        try
        {
            var outcome = await _session.Api.SyncLinkedInPagesAsync();

            if (outcome == LinkedInSyncOutcome.NotLinked)
            {
                // Expected state, not an error — correct the local flag and say
                // what to do about it.
                IsLinkedInLinked = false;
                LinkedInMessage = "LinkedIn isn't connected. Connect it first, then sync pages.";
                return;
            }

            // Read-after-write: the sync response body is unverified, so the
            // truth comes from re-reading targets.
            await LoadLinkedInTargetsAsync();
            LinkedInMessage = LinkedInTargets.Count > 0
                ? $"Synced — {LinkedInTargets.Count} destination(s) available."
                : "Synced, but LinkedIn returned no pages for this account.";
        }
        catch (InterlinedApiException ex)
        {
            LinkedInMessage = ex.Message;
        }
        finally
        {
            IsSyncingLinkedIn = false;
        }
    }
}
