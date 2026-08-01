namespace InterlinedList.Sync.Core;

/// <summary>
/// The repo-wide conflict policy: <b>server wins the canonical file</b>, and a
/// diverged local copy is preserved as <c>&lt;stem&gt;.conflict-&lt;timestamp&gt;.md</c>.
/// This type only makes the decision; the engine performs the file operations.
/// </summary>
public static class ConflictResolver
{
    /// <summary>The 4-way truth table over (local changed, remote changed).</summary>
    public static SyncAction Decide(bool localChanged, bool remoteChanged) =>
        (localChanged, remoteChanged) switch
        {
            (false, false) => SyncAction.NoOp,
            (true, false) => SyncAction.Push,
            (false, true) => SyncAction.Pull,
            (true, true) => SyncAction.ConflictCopyThenPull,
        };
}
