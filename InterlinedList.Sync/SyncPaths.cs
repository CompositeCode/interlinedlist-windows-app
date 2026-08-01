namespace InterlinedList.Sync;

/// <summary>Well-known on-disk locations, all under the app's per-user data folders.</summary>
internal static class SyncPaths
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Shared with the main app — the DPAPI-encrypted bearer sync-token.</summary>
    public static string SessionFile { get; } = Path.Combine(LocalAppData, "InterlinedList", "session.dat");

    /// <summary>Root of the sync utility's own data (state.db, prefs, logs).</summary>
    public static string SyncDataDir { get; } = Path.Combine(LocalAppData, "InterlinedList", "sync");

    public static string StateDbFile { get; } = Path.Combine(SyncDataDir, "state.db");
    public static string PreferencesFile { get; } = Path.Combine(SyncDataDir, "preferences.json");
    public static string LogsDir { get; } = Path.Combine(SyncDataDir, "logs");

    /// <summary>Default sync folder when the user hasn't chosen one yet.</summary>
    public static string DefaultSyncFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "InterlinedList");
}
