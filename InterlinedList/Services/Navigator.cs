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

    public static void OpenProfile(string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            OnOpenProfile?.Invoke(username);
    }
}
