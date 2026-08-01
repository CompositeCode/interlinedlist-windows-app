namespace InterlinedList.Services;

/// <summary>
/// Tiny in-process navigation hub. Lets a feed/search card ask the shell to open
/// a user's profile without holding a reference to MainWindow. A single settable
/// callback (not a multicast event) so re-creating the shell on re-login simply
/// replaces the handler instead of stacking duplicates.
/// </summary>
public static class Navigator
{
    public static Action<string>? OnOpenProfile { get; set; }

    /// <summary>Set by the shell; lets a center view (e.g. account deletion) return the app to the login screen.</summary>
    public static Action? OnLoggedOut { get; set; }

    public static void OpenProfile(string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            OnOpenProfile?.Invoke(username);
    }

    public static void RequestLogout() => OnLoggedOut?.Invoke();
}
