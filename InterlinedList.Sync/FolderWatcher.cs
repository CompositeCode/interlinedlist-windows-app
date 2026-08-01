using System.IO;
using InterlinedList.Sync.Core;

namespace InterlinedList.Sync;

/// <summary>
/// Watches the sync folder for <c>*.md</c> changes and, after a 500ms quiet period
/// (to coalesce an editor's write burst), emits one <see cref="LocalChange"/> per path.
/// </summary>
internal sealed class FolderWatcher : IDisposable
{
    private const int DebounceMs = 500;

    private readonly FileSystemWatcher _fsw;
    private readonly Action<LocalChange> _onChange;
    private readonly object _lock = new();
    private readonly Dictionary<string, LocalChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _debounce;

    public FolderWatcher(string root, Action<LocalChange> onChange)
    {
        _onChange = onChange;
        Directory.CreateDirectory(root);

        _fsw = new FileSystemWatcher(root, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                           NotifyFilters.Size | NotifyFilters.DirectoryName,
            InternalBufferSize = 64 * 1024,
        };
        _fsw.Created += (_, e) => Queue(new LocalChange(LocalChangeKind.CreatedOrModified, e.FullPath));
        _fsw.Changed += (_, e) => Queue(new LocalChange(LocalChangeKind.CreatedOrModified, e.FullPath));
        _fsw.Deleted += (_, e) => Queue(new LocalChange(LocalChangeKind.Deleted, e.FullPath));
        _fsw.Renamed += (_, e) => Queue(new LocalChange(LocalChangeKind.Renamed, e.FullPath, e.OldFullPath));
        _fsw.Error += (_, e) => SyncLog.Error("File watcher error.", e.GetException());

        _debounce = new System.Threading.Timer(Flush, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _fsw.EnableRaisingEvents = true;
    public void Stop() => _fsw.EnableRaisingEvents = false;

    private void Queue(LocalChange change)
    {
        lock (_lock)
        {
            // A rename supersedes a prior pending edit at the new path.
            _pending[change.Path] = change;
            _debounce.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void Flush(object? _)
    {
        List<LocalChange> batch;
        lock (_lock)
        {
            batch = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var change in batch)
        {
            try { _onChange(change); }
            catch (Exception ex) { SyncLog.Error($"Change handler failed for '{change.Path}'.", ex); }
        }
    }

    public void Dispose()
    {
        _fsw.Dispose();
        _debounce.Dispose();
    }
}
