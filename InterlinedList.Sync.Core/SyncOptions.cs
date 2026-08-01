namespace InterlinedList.Sync.Core;

/// <summary>User-tunable sync settings (persisted by the tray app's preferences).</summary>
public sealed class SyncOptions
{
    /// <summary>Absolute path of the local folder to keep in sync (the Obsidian vault).</summary>
    public string SyncFolder { get; set; } = string.Empty;

    /// <summary>Seconds between pull polls (the API recommends ≥ 10s; default 30s).</summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Run a full snapshot reconcile every Nth poll to catch unreliable delete tombstones.</summary>
    public int FullReconcileEveryNPolls { get; set; } = 20;

    /// <summary>Register to launch at login.</summary>
    public bool AutoStart { get; set; } = true;

    public int EffectivePollIntervalSeconds => Math.Max(10, PollIntervalSeconds);
}
