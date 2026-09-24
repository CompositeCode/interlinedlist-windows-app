using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Manage the account's standing sync-tokens, grouped by device label.
/// </summary>
/// <remarks>
/// Grouping is the point. Every row is a never-expiring bearer token, and the
/// list reaches real scale: the test account held <b>1,276</b>, of which
/// <b>1,251</b> shared the label <c>CLI</c>. A flat list of 1,276 rows is not a
/// control a person can act on; "revoke all 1,251 CLI sessions" is.
/// </remarks>
public partial class SessionsPanelViewModel : ObservableObject
{
    private readonly SessionService _session;
    private List<ApiSession> _all = [];

    public ObservableCollection<SessionGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Headline))]
    private int totalCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeAllOthersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Progress text during a sweep; 1,200+ deletes takes minutes.</summary>
    [ObservableProperty]
    private string? progressText;

    [ObservableProperty]
    private double progressFraction;

    /// <summary>Armed by a first press, so a thousand revocations isn't one click.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevokeAllPrompt))]
    private bool isRevokeAllArmed;

    public string Headline => TotalCount == 0
        ? "No active API sessions."
        : $"{TotalCount} active API session(s). Each one is a standing token that never expires.";

    public string RevokeAllPrompt => IsRevokeAllArmed
        ? $"Revoke {RevocableCount} session(s)? This device stays signed in. Press again to confirm."
        : "Revoke all other sessions";

    private int RevocableCount => _all.Count(s => !s.IsCurrent);

    public SessionsPanelViewModel(SessionService session) => _session = session;

    private bool CanLoad() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _all = await _session.Api.GetSessionsAsync();
            TotalCount = _all.Count;
            Rebuild();
            StatusMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsRevokeAllArmed = false;
        }
    }

    private void Rebuild()
    {
        Groups.Clear();
        // Biggest pile first — that's the one worth acting on.
        foreach (var group in _all
            .GroupBy(s => s.DeviceLabelOrFallback, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            Groups.Add(new SessionGroupViewModel(group.Key, [.. group], RevokeGroupAsync));
        }
        RevokeAllOthersCommand.NotifyCanExecuteChanged();
    }

    private bool CanRevokeAll() => !IsBusy && RevocableCount > 0;

    [RelayCommand(CanExecute = nameof(CanRevokeAll))]
    private async Task RevokeAllOthersAsync()
    {
        // Two-step rather than a modal: a native dialog would block the
        // dispatcher, and this is irreversible at scale.
        if (!IsRevokeAllArmed)
        {
            IsRevokeAllArmed = true;
            return;
        }

        IsRevokeAllArmed = false;
        await SweepAsync(_all);
    }

    private Task RevokeGroupAsync(SessionGroupViewModel group) => SweepAsync(group.Sessions);

    private async Task SweepAsync(IReadOnlyList<ApiSession> sessions)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        ProgressFraction = 0;

        var progress = new Progress<BulkRevokeProgress>(p =>
        {
            ProgressFraction = p.Fraction;
            ProgressText = $"Revoking… {p.Completed} of {p.Total} ({p.Revoked} done, {p.Failed} failed)";
        });

        try
        {
            var result = await _session.Api.RevokeSessionsAsync(sessions, progress);
            StatusMessage = result.Summary;
            if (result.Errors.Count > 0)
                ErrorMessage = string.Join("  ·  ", result.Errors);

            // Read-after-write: re-read rather than mutating the local list, so
            // the count and grouping reflect what the server actually has.
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
            ProgressFraction = 0;
        }
    }
}

/// <summary>One device label's worth of sessions.</summary>
public partial class SessionGroupViewModel : ObservableObject
{
    private readonly Func<SessionGroupViewModel, Task> _revoke;

    public string Label { get; }
    public IReadOnlyList<ApiSession> Sessions { get; }
    public int Count => Sessions.Count;

    /// <summary>True when this group holds the caller's own session, which can't be revoked.</summary>
    public bool ContainsCurrent { get; }

    public int RevocableCount { get; }

    public string Detail
    {
        get
        {
            var newest = Sessions.Max(s => s.LastUsedAt ?? s.CreatedAt);
            var suffix = ContainsCurrent ? " · includes this device" : "";
            return $"{Count} session(s) · last used {newest.ToLocalTime():yyyy-MM-dd HH:mm}{suffix}";
        }
    }

    public string RevokeLabel => ContainsCurrent
        ? $"Revoke the other {RevocableCount}"
        : $"Revoke all {Count}";

    public bool CanRevoke => RevocableCount > 0;

    public SessionGroupViewModel(string label, IReadOnlyList<ApiSession> sessions, Func<SessionGroupViewModel, Task> revoke)
    {
        Label = label;
        Sessions = sessions;
        _revoke = revoke;
        ContainsCurrent = sessions.Any(s => s.IsCurrent);
        RevocableCount = sessions.Count(s => !s.IsCurrent);
    }

    [RelayCommand]
    private Task RevokeGroup() => _revoke(this);
}
