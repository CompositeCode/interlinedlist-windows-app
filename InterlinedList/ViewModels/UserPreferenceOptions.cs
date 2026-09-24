namespace InterlinedList.ViewModels;

/// <summary>
/// The accepted wire values for the two enum-shaped user preferences, plus the
/// theme resolution the whole app shares. Kept next to the settings view model
/// because that is the only place that *writes* them, but
/// <see cref="ResolveDark"/> is what <c>App</c> reads to decide a theme.
/// </summary>
/// <remarks>
/// Both value sets were confirmed live 2026-09-16 against
/// <c>PATCH /api/user/update</c>: <c>viewingPreference</c> is enum-validated by
/// the server, which named the four values itself in its 400 body
/// ("viewingPreference must be one of: my_messages, all_messages,
/// followers_only, following_only"). <c>theme</c> is <b>not</b> validated — the
/// server stored an arbitrary string without complaint — hence
/// <see cref="ResolveDark"/> treats anything unrecognized as "follow the OS"
/// instead of assuming the value is one of three.
/// </remarks>
public static class UserPreferenceOptions
{
    // ── theme ───────────────────────────────────────────────────────────────────

    public const string ThemeSystem = "system";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";

    /// <summary>
    /// Theme precedence in one place: an explicit server-side <c>light</c>/
    /// <c>dark</c> wins, and <c>system</c> — or null, or any unvalidated value
    /// the server let through — falls back to the OS preference.
    /// </summary>
    public static bool ResolveDark(string? serverTheme, bool osPrefersDark) =>
        Normalize(serverTheme) switch
        {
            ThemeLight => false,
            ThemeDark => true,
            _ => osPrefersDark
        };

    /// <summary>Maps a stored theme onto one of the three values the picker offers.</summary>
    public static string NormalizeTheme(string? serverTheme) =>
        Normalize(serverTheme) switch
        {
            ThemeLight => ThemeLight,
            ThemeDark => ThemeDark,
            _ => ThemeSystem
        };

    // ── viewingPreference ───────────────────────────────────────────────────────

    public const string ViewingMyMessages = "my_messages";
    public const string ViewingAllMessages = "all_messages";
    public const string ViewingFollowersOnly = "followers_only";
    public const string ViewingFollowingOnly = "following_only";

    /// <summary>Falls back to <c>all_messages</c> for an unknown/empty value.</summary>
    public static string NormalizeViewingPreference(string? value) =>
        Normalize(value) switch
        {
            ViewingMyMessages => ViewingMyMessages,
            ViewingFollowersOnly => ViewingFollowersOnly,
            ViewingFollowingOnly => ViewingFollowingOnly,
            _ => ViewingAllMessages
        };

    // ── server-enforced ranges (each learned from a live 400) ───────────────────

    public const int MinMaxMessageLength = 1;
    public const int MaxMaxMessageLength = 10000;
    public const int MinMessagesPerPage = 10;
    public const int MaxMessagesPerPage = 30;
    public const int MinNotificationTrayLimit = 10;
    public const int MaxNotificationTrayLimit = 40;

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? "";
}
