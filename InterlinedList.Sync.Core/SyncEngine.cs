namespace InterlinedList.Sync.Core;

/// <summary>
/// The bidirectional document-sync engine. Pull (server → local files) materializes
/// the server's folder tree as real subdirectories and resolves divergence with the
/// server-wins/conflict-copy policy; push (local files → server) mirrors Obsidian
/// edits back, creating server folders on demand. Deliberately transport- and
/// clock-injected so the whole thing is unit-testable off-Windows.
/// </summary>
public sealed class SyncEngine
{
    private readonly IDocumentSyncClient _client;
    private readonly ISyncStateStore _state;
    private readonly FileMapper _mapper;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string>? _trace;

    public SyncEngine(
        IDocumentSyncClient client,
        ISyncStateStore state,
        FileMapper mapper,
        Func<DateTimeOffset>? now = null,
        Action<string>? trace = null)
    {
        _client = client;
        _state = state;
        _mapper = mapper;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _trace = trace;
    }

    // ── Pull: server → local ─────────────────────────────────────────────────────

    public Task<SyncReport> SyncNowAsync(CancellationToken ct = default) => PullAsync(false, ct);

    public async Task<SyncReport> PullAsync(bool fullSnapshot, CancellationToken ct = default)
    {
        var report = new SyncReport { FullSnapshot = fullSnapshot };
        var since = fullSnapshot ? null : _state.GetLastSyncAt();
        var delta = fullSnapshot
            ? await _client.GetFullSnapshotAsync(ct)
            : await _client.GetDeltaAsync(since, ct);

        Directory.CreateDirectory(_mapper.Root);

        var folderPaths = ApplyFolders(delta.Folders, report);

        foreach (var doc in delta.Documents)
        {
            ct.ThrowIfCancellationRequested();
            ApplyDocument(doc, folderPaths, report);
        }

        // A folder rename with no document changes still relocates the files inside
        // it — realign every tracked doc whose folder moved.
        RealignTrackedDocumentPaths(folderPaths, report);

        if (fullSnapshot)
            ReconcileDeletions(delta, report);

        if (delta.LastSyncAt is { } cursor)
            _state.SetLastSyncAt(cursor);

        _trace?.Invoke($"pull: {report}");
        return report;
    }

    /// <summary>
    /// Merge the (possibly partial) folder delta with what we already know, recompute
    /// every folder's sanitized relative path, persist it, and ensure directories
    /// exist. Returns the id → relative-path map used for document placement.
    /// </summary>
    private Dictionary<string, string> ApplyFolders(IReadOnlyList<RemoteFolder> deltaFolders, SyncReport report)
    {
        // Merge existing + delta into an authoritative id → (name, parentId) view.
        var merged = new Dictionary<string, (string Name, string? ParentId)>();
        foreach (var f in _state.AllFolders())
            merged[f.Id] = (f.Name, f.ParentId);
        foreach (var f in deltaFolders)
            merged[f.Id] = (f.Name, f.ParentId);

        var relPaths = new Dictionary<string, string>();
        foreach (var id in merged.Keys)
            relPaths[id] = ComputeRelativePath(id, merged);

        // Shallow → deep so parents are created before children.
        foreach (var id in relPaths.Keys.OrderBy(k => relPaths[k].Count(c => c == Path.DirectorySeparatorChar)))
        {
            var (name, parentId) = merged[id];
            var rel = relPaths[id];
            var abs = _mapper.FolderPath(rel);
            var existing = _state.GetFolder(id);

            if (existing is null)
                report.FoldersCreated++;

            Directory.CreateDirectory(abs);
            _state.UpsertFolder(new FolderRecord(id, parentId, name, rel));

            // Old directory (pre-rename) may now be empty — clean up best-effort.
            if (existing is not null && !PathsEqual(existing.RelativePath, rel))
                TryRemoveEmptyDirectory(_mapper.FolderPath(existing.RelativePath));
        }

        return relPaths;
    }

    private static string ComputeRelativePath(string id, Dictionary<string, (string Name, string? ParentId)> merged)
    {
        var segments = new List<string>();
        var seen = new HashSet<string>();
        var current = id;
        while (current is not null && merged.TryGetValue(current, out var node) && seen.Add(current))
        {
            segments.Add(PathSanitizer.Sanitize(node.Name));
            current = node.ParentId;
        }
        segments.Reverse();
        return segments.Count == 0 ? string.Empty : Path.Combine([.. segments]);
    }

    private void ApplyDocument(RemoteDocument doc, Dictionary<string, string> folderPaths, SyncReport report)
    {
        var record = _state.GetDocument(doc.Id);

        if (doc.IsDeleted)
        {
            if (record is not null)
            {
                TryDeleteFile(record.LocalPath);
                _state.DeleteDocument(doc.Id);
                report.Deleted++;
            }
            return;
        }

        var relDir = ResolveFolderRelativePath(doc.FolderId, folderPaths);
        var targetPath = _mapper.DocumentPath(relDir, doc.Title);

        // A content-less entry (e.g. from a lightweight full-list snapshot) must never
        // overwrite a good local file. Realign path/folder metadata only.
        if (doc.Content is null)
        {
            if (record is null)
            {
                _trace?.Invoke($"warn: snapshot doc '{doc.Id}' has no content, deferring create to next delta");
                return;
            }
            if (!PathsEqual(record.LocalPath, targetPath))
            {
                MoveFile(record.LocalPath, targetPath);
                _state.UpsertDocument(record with { LocalPath = targetPath, FolderId = doc.FolderId });
            }
            else if (record.FolderId != doc.FolderId)
            {
                _state.UpsertDocument(record with { FolderId = doc.FolderId });
            }
            return;
        }

        var remoteContent = doc.Content;
        var remoteHash = ContentHash.Of(remoteContent);

        if (record is null)
        {
            // New server document. Guard against clobbering an unrelated local file
            // that happens to share the path: keep it as a conflict copy first.
            if (File.Exists(targetPath) && ContentHash.Of(ReadAllText(targetPath)) != remoteHash)
            {
                CopyToConflict(targetPath, report);
            }
            WriteFileAtomic(targetPath, remoteContent);
            _state.UpsertDocument(new DocumentRecord(doc.Id, targetPath, doc.FolderId, doc.UpdatedAt, remoteHash));
            report.Pulled++;
            return;
        }

        var pathChanged = !PathsEqual(record.LocalPath, targetPath);
        var localOnDisk = File.Exists(record.LocalPath) ? ContentHash.Of(ReadAllText(record.LocalPath)) : null;
        var localChanged = localOnDisk is not null && localOnDisk != record.ContentHash;
        var remoteChanged = remoteHash != record.ContentHash;

        switch (ConflictResolver.Decide(localChanged, remoteChanged))
        {
            case SyncAction.ConflictCopyThenPull:
                CopyToConflict(record.LocalPath, report);
                MaterializeAt(targetPath, record.LocalPath, pathChanged, remoteContent);
                _state.UpsertDocument(new DocumentRecord(doc.Id, targetPath, doc.FolderId, doc.UpdatedAt, remoteHash));
                report.Pulled++;
                break;

            case SyncAction.Pull:
                MaterializeAt(targetPath, record.LocalPath, pathChanged, remoteContent);
                _state.UpsertDocument(new DocumentRecord(doc.Id, targetPath, doc.FolderId, doc.UpdatedAt, remoteHash));
                report.Pulled++;
                break;

            case SyncAction.Push:
            case SyncAction.NoOp:
                // Remote content unchanged. If the server renamed/moved it, relocate
                // the (possibly locally-edited) file without touching its contents.
                if (pathChanged)
                {
                    MoveFile(record.LocalPath, targetPath);
                    _state.UpsertDocument(record with { LocalPath = targetPath, FolderId = doc.FolderId });
                }
                else if (record.FolderId != doc.FolderId || record.ServerUpdatedAt != doc.UpdatedAt)
                {
                    _state.UpsertDocument(record with { FolderId = doc.FolderId, ServerUpdatedAt = doc.UpdatedAt });
                }
                break;
        }
    }

    private void RealignTrackedDocumentPaths(Dictionary<string, string> folderPaths, SyncReport report)
    {
        foreach (var rec in _state.AllDocuments())
        {
            var relDir = ResolveFolderRelativePath(rec.FolderId, folderPaths);
            var fileName = Path.GetFileName(rec.LocalPath);
            var expected = string.IsNullOrEmpty(relDir)
                ? Path.Combine(_mapper.Root, fileName)
                : Path.Combine(_mapper.Root, relDir, fileName);

            if (PathsEqual(expected, rec.LocalPath))
                continue;

            MoveFile(rec.LocalPath, expected);
            _state.UpsertDocument(rec with { LocalPath = expected });
        }
    }

    private void ReconcileDeletions(SyncDelta snapshot, SyncReport report)
    {
        var liveDocIds = snapshot.Documents.Where(d => !d.IsDeleted).Select(d => d.Id).ToHashSet();
        foreach (var rec in _state.AllDocuments())
        {
            if (liveDocIds.Contains(rec.Id)) continue;
            TryDeleteFile(rec.LocalPath);
            _state.DeleteDocument(rec.Id);
            report.Deleted++;
        }

        var liveFolderIds = snapshot.Folders.Select(f => f.Id).ToHashSet();
        foreach (var f in _state.AllFolders())
        {
            if (liveFolderIds.Contains(f.Id)) continue;
            _state.DeleteFolder(f.Id);
            TryRemoveEmptyDirectory(_mapper.FolderPath(f.RelativePath));
        }
    }

    // ── Push: local → server ─────────────────────────────────────────────────────

    public async Task PushLocalChangeAsync(LocalChange change, CancellationToken ct = default)
    {
        if (_mapper.IsConflictFile(change.Path))
            return;

        switch (change.Kind)
        {
            case LocalChangeKind.Deleted:
                await PushDeleteAsync(change.Path, ct);
                break;

            case LocalChangeKind.Renamed when _state.GetDocumentByPath(change.OldPath ?? string.Empty) is { } moved:
                await PushMoveOrEditAsync(moved, change.Path, ct);
                break;

            case LocalChangeKind.Renamed:
            case LocalChangeKind.CreatedOrModified:
                await PushCreateOrEditAsync(change.Path, ct);
                break;
        }
    }

    private async Task PushCreateOrEditAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        var content = ReadAllText(path);
        var hash = ContentHash.Of(content);
        var title = Path.GetFileNameWithoutExtension(path);
        var record = _state.GetDocumentByPath(path);

        if (record is not null)
        {
            if (hash == record.ContentHash) return; // echo of our own write — ignore
            var updated = await _client.UpdateDocumentAsync(record.Id, title, content, record.FolderId, ct);
            _state.UpsertDocument(new DocumentRecord(record.Id, path, record.FolderId, updated.UpdatedAt, hash));
            _trace?.Invoke($"push: updated '{title}'");
            return;
        }

        var folderId = await ResolveOrCreateFolderAsync(path, ct);
        var created = await _client.CreateDocumentAsync(title, content, folderId, ct);
        _state.UpsertDocument(new DocumentRecord(created.Id, path, folderId, created.UpdatedAt, hash));
        _trace?.Invoke($"push: created '{title}' in folder '{folderId ?? "(root)"}'");
    }

    private async Task PushMoveOrEditAsync(DocumentRecord record, string newPath, CancellationToken ct)
    {
        var content = File.Exists(newPath) ? ReadAllText(newPath) : string.Empty;
        var title = Path.GetFileNameWithoutExtension(newPath);
        var folderId = await ResolveOrCreateFolderAsync(newPath, ct);
        var updated = await _client.UpdateDocumentAsync(record.Id, title, content, folderId, ct);
        _state.DeleteDocument(record.Id);
        _state.UpsertDocument(new DocumentRecord(record.Id, newPath, folderId, updated.UpdatedAt, ContentHash.Of(content)));
        _trace?.Invoke($"push: moved/renamed '{title}' → folder '{folderId ?? "(root)"}'");
    }

    private async Task PushDeleteAsync(string path, CancellationToken ct)
    {
        var record = _state.GetDocumentByPath(path);
        if (record is null) return;
        await _client.DeleteDocumentAsync(record.Id, ct);
        _state.DeleteDocument(record.Id);
        _trace?.Invoke($"push: deleted '{Path.GetFileName(path)}'");
    }

    /// <summary>
    /// Resolve the server folderId for a local file, creating the folder chain
    /// server-side (and recording it) for any subdirectories that don't exist yet.
    /// Returns null for files at the vault root.
    /// </summary>
    private async Task<string?> ResolveOrCreateFolderAsync(string filePath, CancellationToken ct)
    {
        var relDir = _mapper.RelativeDirectoryOf(filePath);
        if (string.IsNullOrEmpty(relDir))
            return null;

        var existing = _state.GetFolderByRelativePath(relDir);
        if (existing is not null)
            return existing.Id;

        // Walk the segments, creating each missing folder under its parent.
        string? parentId = null;
        var accumulated = string.Empty;
        foreach (var segment in relDir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = string.IsNullOrEmpty(accumulated) ? segment : Path.Combine(accumulated, segment);
            var known = _state.GetFolderByRelativePath(accumulated);
            if (known is not null)
            {
                parentId = known.Id;
                continue;
            }

            var created = await _client.CreateFolderAsync(segment, parentId, ct);
            _state.UpsertFolder(new FolderRecord(created.Id, parentId, created.Name, accumulated));
            parentId = created.Id;
        }

        return parentId;
    }

    // ── File helpers ─────────────────────────────────────────────────────────────

    private string ResolveFolderRelativePath(string? folderId, Dictionary<string, string> folderPaths)
    {
        if (folderId is null) return string.Empty;
        if (folderPaths.TryGetValue(folderId, out var rel)) return rel;
        // Unknown folder (delta arrived out of order) — fall back to root rather than throw.
        _trace?.Invoke($"warn: unknown folderId '{folderId}', placing at root");
        return string.Empty;
    }

    private void MaterializeAt(string targetPath, string oldPath, bool pathChanged, string content)
    {
        if (pathChanged) TryDeleteFile(oldPath);
        WriteFileAtomic(targetPath, content);
    }

    private void CopyToConflict(string path, SyncReport report)
    {
        if (!File.Exists(path)) return;
        var conflictPath = _mapper.ConflictPath(path, _now());
        File.Copy(path, conflictPath, overwrite: true);
        report.Conflicts++;
        _trace?.Invoke($"conflict: kept local copy '{Path.GetFileName(conflictPath)}'");
    }

    private static void WriteFileAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static void MoveFile(string from, string to)
    {
        if (!File.Exists(from))
            return;
        var dir = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Move(from, to, overwrite: true);
    }

    private static string ReadAllText(string path) => File.ReadAllText(path);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch { /* best effort */ }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
