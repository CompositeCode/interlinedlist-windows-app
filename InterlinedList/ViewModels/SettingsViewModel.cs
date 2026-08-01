using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;
using Microsoft.Win32;

namespace InterlinedList.ViewModels;

/// <summary>
/// Settings view model: edit profile, manage standing API sessions (sync-tokens),
/// notification preferences, and the blocked/muted user lists. Sessions are NOT
/// auto-loaded (the list can be hundreds of rows) — they load on the Refresh
/// button via <see cref="LoadSessionsCommand"/>.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<ApiSession> Sessions { get; } = new();
    public ObservableCollection<NotificationPrefViewModel> Preferences { get; } = new();
    public ObservableCollection<ModeratedUser> BlockedUsers { get; } = new();
    public ObservableCollection<ModeratedUser> MutedUsers { get; } = new();

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string bio = "";

    [ObservableProperty]
    private bool isPrivateAccount;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string avatarUrl = "";

    [ObservableProperty]
    private string newEmail = "";

    public SettingsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task SetAvatarAsync()
    {
        if (string.IsNullOrWhiteSpace(AvatarUrl)) return;
        try
        {
            await _session.Api.UpdateAvatarFromUrlAsync(AvatarUrl.Trim());
            AvatarUrl = "";
            ErrorMessage = "Avatar updated.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // Billing/subscription is cookie-session-only server-side, so the native app
    // hands off to the website (same pattern as OAuth linking).
    [RelayCommand]
    private void OpenWebAccount()
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ApiConfig.BaseUrl,
            UseShellExecute = true
        });

    [RelayCommand]
    private async Task ChangeEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEmail)) return;
        try
        {
            await _session.Api.RequestEmailChangeAsync(NewEmail.Trim());
            NewEmail = "";
            ErrorMessage = "Requested — check your inbox to confirm the new address.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            // Prefill profile fields from the current user.
            var user = _session.CurrentUser;
            if (user is not null)
            {
                DisplayName = user.DisplayName ?? "";
                Bio = user.Bio ?? "";
                IsPrivateAccount = user.IsPrivateAccount;
            }

            var prefs = await _session.Api.GetNotificationPreferencesAsync();
            Preferences.Clear();
            foreach (var pref in prefs)
                Preferences.Add(new NotificationPrefViewModel(pref, _session));

            var blocked = await _session.Api.GetBlockedUsersAsync();
            BlockedUsers.Clear();
            foreach (var blockedUser in blocked)
                BlockedUsers.Add(blockedUser);

            var muted = await _session.Api.GetMutedUsersAsync();
            MutedUsers.Clear();
            foreach (var mutedUser in muted)
                MutedUsers.Add(mutedUser);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadSessionsAsync()
    {
        IsBusy = true;
        try
        {
            var sessions = await _session.Api.GetSessionsAsync();
            Sessions.Clear();
            foreach (var s in sessions)
                Sessions.Add(s);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        IsBusy = true;
        try
        {
            await _session.Api.UpdateProfileAsync(DisplayName, Bio, IsPrivateAccount);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RevokeAsync(ApiSession s)
    {
        try
        {
            await _session.Api.RevokeSessionAsync(s.Id);
            Sessions.Remove(s);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UnblockAsync(ModeratedUser u)
    {
        try
        {
            await _session.Api.UnblockUserAsync(u.Username);
            BlockedUsers.Remove(u);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UnmuteAsync(ModeratedUser u)
    {
        try
        {
            await _session.Api.UnmuteUserAsync(u.Username);
            MutedUsers.Remove(u);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── CSV data export ─────────────────────────────────────────────────────────

    [RelayCommand]
    private Task ExportMessagesAsync() => ExportCsvAsync(_session.Api.ExportMessagesCsvAsync, "messages.csv");

    [RelayCommand]
    private Task ExportListsAsync() => ExportCsvAsync(_session.Api.ExportListsCsvAsync, "lists.csv");

    [RelayCommand]
    private Task ExportListRowsAsync() => ExportCsvAsync(_session.Api.ExportListDataRowsCsvAsync, "list-rows.csv");

    [RelayCommand]
    private Task ExportFollowsAsync() => ExportCsvAsync(_session.Api.ExportFollowsCsvAsync, "follows.csv");

    private async Task ExportCsvAsync(Func<CancellationToken, Task<string>> fetch, string defaultFileName)
    {
        try
        {
            var csv = await fetch(default);
            var dlg = new SaveFileDialog
            {
                FileName = defaultFileName,
                DefaultExt = ".csv",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                await File.WriteAllTextAsync(dlg.FileName, csv);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (IOException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
