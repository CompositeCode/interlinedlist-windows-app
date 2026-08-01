using System.Text.Json;
using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

/// <summary>Loads/saves <see cref="SyncOptions"/> as JSON (atomic temp+move).</summary>
internal static class SyncPreferencesStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SyncOptions Load()
    {
        try
        {
            if (File.Exists(SyncPaths.PreferencesFile))
            {
                var opts = JsonSerializer.Deserialize<SyncOptions>(File.ReadAllText(SyncPaths.PreferencesFile), Json);
                if (opts is not null)
                {
                    if (string.IsNullOrWhiteSpace(opts.SyncFolder)) opts.SyncFolder = SyncPaths.DefaultSyncFolder;
                    return opts;
                }
            }
        }
        catch (Exception ex)
        {
            SyncLog.Warn($"Failed to read preferences; using defaults. {ex.Message}");
        }

        return new SyncOptions { SyncFolder = SyncPaths.DefaultSyncFolder };
    }

    public static void Save(SyncOptions options)
    {
        try
        {
            Directory.CreateDirectory(SyncPaths.SyncDataDir);
            var tmp = SyncPaths.PreferencesFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(options, Json));
            File.Move(tmp, SyncPaths.PreferencesFile, overwrite: true);
        }
        catch (Exception ex)
        {
            SyncLog.Error("Failed to save preferences.", ex);
        }
    }
}
