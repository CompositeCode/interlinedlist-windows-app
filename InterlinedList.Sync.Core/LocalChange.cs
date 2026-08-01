namespace InterlinedList.Sync.Core;

/// <summary>The kind of local filesystem change the watcher observed.</summary>
public enum LocalChangeKind
{
    CreatedOrModified,
    Deleted,
    Renamed,
}

/// <summary>A debounced local file change to push to the server.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Path">Current absolute path of the file.</param>
/// <param name="OldPath">Previous absolute path (only for <see cref="LocalChangeKind.Renamed"/>).</param>
public sealed record LocalChange(LocalChangeKind Kind, string Path, string? OldPath = null);

/// <summary>Tally of what a sync cycle did — surfaced to logs and the tray tooltip.</summary>
public sealed class SyncReport
{
    public int Pulled { get; set; }
    public int Pushed { get; set; }
    public int Deleted { get; set; }
    public int Conflicts { get; set; }
    public int FoldersCreated { get; set; }
    public bool FullSnapshot { get; set; }

    public override string ToString() =>
        $"pulled={Pulled} pushed={Pushed} deleted={Deleted} conflicts={Conflicts} " +
        $"foldersCreated={FoldersCreated} full={FullSnapshot}";
}
