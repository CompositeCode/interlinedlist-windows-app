using System.Collections.ObjectModel;
using System.Globalization;
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

    // Account deletion requires typing your exact username as a guard.
    [ObservableProperty]
    private string deleteConfirmUsername = "";

    // ── Server-side preferences (see #46) ───────────────────────────────────────
    // These mirror the writable fields on PATCH /api/user/update. They are the
    // *edit* surface; each one also has a consumption point elsewhere in the app
    // that has to obey it, which is the other half of #46.

    // ── Account standing (see #50) ──────────────────────────────────────────────
    // Recomputed on every CurrentUser snapshot. Null until the first load; the
    // banner is collapsed for a normal `active` account.
    [ObservableProperty]
    private AccountStatusViewModel? accountStatus;

    [ObservableProperty]
    private string theme = UserPreferenceOptions.ThemeSystem;

    [ObservableProperty]
    private string viewingPreference = UserPreferenceOptions.ViewingAllMessages;

    [ObservableProperty]
    private string maxMessageLength = "666";

    [ObservableProperty]
    private string messagesPerPage = "20";

    [ObservableProperty]
    private string notificationTrayLimit = "20";

    [ObservableProperty]
    private bool showPreviews;

    [ObservableProperty]
    private bool defaultPubliclyVisible;

    [ObservableProperty]
    private bool showAdvancedPostSettings;

    [ObservableProperty]
    private string latitude = "";

    [ObservableProperty]
    private string longitude = "";

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
    private void OpenWebAccount() => OpenInBrowser(ApiConfig.BaseUrl);

    /// <summary>
    /// The account-status banner's call to action — verify your email, or appeal.
    /// Both are browser handoffs: <c>POST /api/auth/send-verification-email</c> is
    /// cookie-session-only per the OpenAPI spec (<c>x-auth-type: session</c>), so
    /// a bearer-token client structurally can't trigger it, and there's no appeal
    /// endpoint at all.
    /// </summary>
    [RelayCommand]
    private void OpenAccountStatusAction()
    {
        if (AccountStatus?.ActionUrl is { Length: > 0 } url)
            OpenInBrowser(url);
    }

    private static void OpenInBrowser(string url)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
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
            // Prefill profile + preference fields from the current user.
            PrefillFromUser(_session.CurrentUser);

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
            await RefreshCurrentUserAsync();
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

    // ── Preferences ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the preference block, then re-reads GET /api/user so the rest of
    /// the app sees the change immediately. The re-read is deliberate: the PATCH
    /// 200 does return a user object, but a narrower one than GET (no
    /// accountStatus/cleared/pendingEmail), so trusting it would blank state
    /// other views depend on.
    /// </summary>
    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        if (!TryParsePreferenceNumbers(out var maxLen, out var perPage, out var trayLimit)
            || !TryParseCoordinates(out var lat, out var lon))
        {
            return; // ErrorMessage already set with the offending range.
        }

        IsBusy = true;
        try
        {
            await _session.Api.UpdatePreferencesAsync(
                theme: UserPreferenceOptions.NormalizeTheme(Theme),
                maxMessageLength: maxLen,
                messagesPerPage: perPage,
                viewingPreference: UserPreferenceOptions.NormalizeViewingPreference(ViewingPreference),
                showPreviews: ShowPreviews,
                notificationTrayLimit: trayLimit,
                defaultPubliclyVisible: DefaultPubliclyVisible,
                showAdvancedPostSettings: ShowAdvancedPostSettings,
                latitude: lat,
                longitude: lon);

            // Assigning CurrentUser raises PropertyChanged, which is what makes a
            // theme change take effect without a restart (App listens for it).
            await RefreshCurrentUserAsync();
            ErrorMessage = "Preferences saved.";
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

    /// <summary>Discards local edits and re-reads the server's values.</summary>
    [RelayCommand]
    private async Task ReloadPreferencesAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshCurrentUserAsync();
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

    private async Task RefreshCurrentUserAsync()
    {
        _session.CurrentUser = await _session.Api.GetCurrentUserAsync();
        PrefillFromUser(_session.CurrentUser);
    }

    private void PrefillFromUser(CurrentUser? user)
    {
        AccountStatus = new AccountStatusViewModel(user);
        if (user is null) return;

        DisplayName = user.DisplayName ?? "";
        Bio = user.Bio ?? "";
        IsPrivateAccount = user.IsPrivateAccount;

        Theme = UserPreferenceOptions.NormalizeTheme(user.Theme);
        ViewingPreference = UserPreferenceOptions.NormalizeViewingPreference(user.ViewingPreference);
        // Show the *effective* paging values, not raw zeros, when the server
        // hasn't set them — otherwise the boxes read "0" and a save would 400.
        MaxMessageLength = (user.MaxMessageLength > 0 ? user.MaxMessageLength : 666).ToString(CultureInfo.InvariantCulture);
        MessagesPerPage = user.EffectiveMessagesPerPage.ToString(CultureInfo.InvariantCulture);
        NotificationTrayLimit = user.EffectiveNotificationTrayLimit.ToString(CultureInfo.InvariantCulture);
        ShowPreviews = user.ShowPreviews;
        DefaultPubliclyVisible = user.DefaultPubliclyVisible;
        ShowAdvancedPostSettings = user.ShowAdvancedPostSettings;
        Latitude = user.Latitude?.ToString(CultureInfo.InvariantCulture) ?? "";
        Longitude = user.Longitude?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    // Validate client-side against the ranges the server itself enforces, so a
    // typo surfaces inline instead of as a raw 400 from the API.
    private bool TryParsePreferenceNumbers(out int maxLen, out int perPage, out int trayLimit)
    {
        maxLen = perPage = trayLimit = 0;

        if (!TryParseRange(MaxMessageLength,
                UserPreferenceOptions.MinMaxMessageLength, UserPreferenceOptions.MaxMaxMessageLength,
                "Max message length", out maxLen))
            return false;

        if (!TryParseRange(MessagesPerPage,
                UserPreferenceOptions.MinMessagesPerPage, UserPreferenceOptions.MaxMessagesPerPage,
                "Messages per page", out perPage))
            return false;

        return TryParseRange(NotificationTrayLimit,
            UserPreferenceOptions.MinNotificationTrayLimit, UserPreferenceOptions.MaxNotificationTrayLimit,
            "Notification tray limit", out trayLimit);
    }

    private bool TryParseRange(string text, int min, int max, string label, out int value)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || value < min || value > max)
        {
            ErrorMessage = $"{label} must be a whole number between {min} and {max}.";
            return false;
        }

        return true;
    }

    // Latitude/longitude are optional: blank clears nothing (the field is simply
    // omitted from the PATCH) because sending null is a 500 server-side.
    private bool TryParseCoordinates(out double? latitude, out double? longitude)
    {
        latitude = longitude = null;

        if (!TryParseOptionalDegrees(Latitude, -90, 90, "Latitude", out latitude)) return false;
        return TryParseOptionalDegrees(Longitude, -180, 180, "Longitude", out longitude);
    }

    private bool TryParseOptionalDegrees(string text, double min, double max, string label, out double? value)
    {
        value = null;
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0) return true;

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed < min || parsed > max)
        {
            ErrorMessage = $"{label} must be a number between {min} and {max}, or blank.";
            return false;
        }

        value = parsed;
        return true;
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

    // Destructive. Enabled only when the typed username matches exactly. On
    // success the token is cleared and the shell returns to the login screen.
    private bool CanDeleteAccount() =>
        _session.CurrentUser is { } u &&
        string.Equals(DeleteConfirmUsername.Trim(), u.Username, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanDeleteAccount))]
    private async Task DeleteAccountAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        IsBusy = true;
        try
        {
            await _session.Api.DeleteAccountAsync(user.Username, user.Email);
            // Local-only teardown on purpose: the account (and with it every
            // sync-token it owned) is already gone, so POST /api/auth/logout and
            // the session-revoke call would just be two 401s on the way out.
            _session.ClearLocalSession();
            Navigator.RequestLogout();
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

    partial void OnDeleteConfirmUsernameChanged(string value) => DeleteAccountCommand.NotifyCanExecuteChanged();

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
