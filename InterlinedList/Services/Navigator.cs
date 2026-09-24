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

    /// <summary>Open a specific message (its thread). Set by the shell.</summary>
    public static Action<string>? OnOpenMessage { get; set; }

    /// <summary>Open a specific list. Set by the shell.</summary>
    public static Action<string>? OnOpenList { get; set; }

    /// <summary>Open a specific organization. Set by the shell.</summary>
    public static Action<string>? OnOpenOrganization { get; set; }

    /// <summary>Open the Connected Accounts view. Set by the shell.</summary>
    public static Action? OnOpenConnectedAccounts { get; set; }

    /// <summary>Set by the shell; lets a center view (e.g. account deletion) return the app to the login screen.</summary>
    public static Action? OnLoggedOut { get; set; }

    public static void OpenProfile(string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            OnOpenProfile?.Invoke(username);
    }

    public static void OpenMessage(string messageId)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
            OnOpenMessage?.Invoke(messageId);
    }

    public static void OpenList(string listId)
    {
        if (!string.IsNullOrWhiteSpace(listId))
            OnOpenList?.Invoke(listId);
    }

    public static void OpenOrganization(string organizationId)
    {
        if (!string.IsNullOrWhiteSpace(organizationId))
            OnOpenOrganization?.Invoke(organizationId);
    }

    public static void OpenConnectedAccounts() => OnOpenConnectedAccounts?.Invoke();

    public static void RequestLogout() => OnLoggedOut?.Invoke();
}
