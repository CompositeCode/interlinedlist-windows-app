using InterlinedList.Sync.Core;
using Xunit;

namespace InterlinedList.Sync.Core.Tests;

/// <summary>Records interactions and returns canned deltas — no network.</summary>
internal sealed class FakeClient : IDocumentSyncClient
{
    public Queue<SyncDelta> Deltas { get; } = new();
    public SyncDelta FullSnapshot { get; set; } = new(null, [], []);

    public List<(string Title, string Content, string? FolderId)> Created { get; } = [];
    public List<(string Id, string Title, string Content, string? FolderId)> Updated { get; } = [];
    public List<string> DeletedIds { get; } = [];
    public List<(string Name, string? ParentId)> FoldersCreated { get; } = [];

    private int _docSeq;
    private int _folderSeq;

    public Task<SyncDelta> GetDeltaAsync(DateTimeOffset? since, CancellationToken ct) =>
        Task.FromResult(Deltas.Count > 0 ? Deltas.Dequeue() : new SyncDelta(null, [], []));

    public Task<SyncDelta> GetFullSnapshotAsync(CancellationToken ct) => Task.FromResult(FullSnapshot);

    public Task<RemoteDocument> CreateDocumentAsync(string title, string content, string? folderId, CancellationToken ct)
    {
        Created.Add((title, content, folderId));
        return Task.FromResult(new RemoteDocument($"doc{++_docSeq}", title, content, folderId, DateTimeOffset.UtcNow, null));
    }

    public Task<RemoteDocument> UpdateDocumentAsync(string id, string title, string content, string? folderId, CancellationToken ct)
    {
        Updated.Add((id, title, content, folderId));
        return Task.FromResult(new RemoteDocument(id, title, content, folderId, DateTimeOffset.UtcNow, null));
    }

    public Task DeleteDocumentAsync(string id, CancellationToken ct)
    {
        DeletedIds.Add(id);
        return Task.CompletedTask;
    }

    public Task<RemoteFolder> CreateFolderAsync(string name, string? parentId, CancellationToken ct)
    {
        FoldersCreated.Add((name, parentId));
        return Task.FromResult(new RemoteFolder($"fld{++_folderSeq}", name, parentId));
    }
}

public sealed class SyncEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "il-sync-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteSyncStateStore _state = SqliteSyncStateStore.InMemory();
    private readonly FakeClient _client = new();
    private readonly SyncEngine _engine;
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    public SyncEngineTests()
    {
        Directory.CreateDirectory(_root);
        _engine = new SyncEngine(_client, _state, new FileMapper(_root), now: () => T0);
    }

    private static RemoteDocument Doc(string id, string title, string content, string? folderId = null, DateTimeOffset? updated = null, DateTimeOffset? deleted = null)
        => new(id, title, content, folderId, updated ?? T0, deleted);

    private string PathIn(params string[] parts) => Path.Combine([_root, .. parts]);

    // ── Pull: server → local ─────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_materializes_document_into_folder_tree()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0,
            [Doc("d1", "Roadmap", "# Roadmap", folderId: "f1")],
            [new RemoteFolder("f1", "Projects", null)]));

        var report = await _engine.PullAsync(false);

        Assert.Equal("# Roadmap", await File.ReadAllTextAsync(PathIn("Projects", "Roadmap.md")));
        Assert.Equal(1, report.Pulled);
        Assert.Equal(1, report.FoldersCreated);
    }

    [Fact]
    public async Task Pull_nested_folder_tree_is_mirrored()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0,
            [Doc("d1", "Deep", "x", folderId: "child")],
            [new RemoteFolder("root", "A", null), new RemoteFolder("child", "B", "root")]));

        await _engine.PullAsync(false);

        Assert.True(File.Exists(PathIn("A", "B", "Deep.md")));
    }

    [Fact]
    public async Task Pull_title_rename_moves_the_file()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Old", "same")], []));
        await _engine.PullAsync(false);
        Assert.True(File.Exists(PathIn("Old.md")));

        // Same content, new title, later timestamp ⇒ server rename.
        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1), [Doc("d1", "New", "same", updated: T0.AddMinutes(1))], []));
        await _engine.PullAsync(false);

        Assert.False(File.Exists(PathIn("Old.md")));
        Assert.True(File.Exists(PathIn("New.md")));
    }

    [Fact]
    public async Task Pull_folder_rename_relocates_contained_documents()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0,
            [Doc("d1", "Note", "body", folderId: "f1")],
            [new RemoteFolder("f1", "Projects", null)]));
        await _engine.PullAsync(false);
        Assert.True(File.Exists(PathIn("Projects", "Note.md")));

        // Only the folder changes name — the document delta is empty.
        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1), [], [new RemoteFolder("f1", "Renamed", null)]));
        await _engine.PullAsync(false);

        Assert.True(File.Exists(PathIn("Renamed", "Note.md")));
        Assert.False(File.Exists(PathIn("Projects", "Note.md")));
    }

    [Fact]
    public async Task Pull_document_moved_between_folders()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0,
            [Doc("d1", "Note", "body", folderId: "f1")],
            [new RemoteFolder("f1", "Alpha", null), new RemoteFolder("f2", "Beta", null)]));
        await _engine.PullAsync(false);
        Assert.True(File.Exists(PathIn("Alpha", "Note.md")));

        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1),
            [Doc("d1", "Note", "body", folderId: "f2", updated: T0.AddMinutes(1))], []));
        await _engine.PullAsync(false);

        Assert.True(File.Exists(PathIn("Beta", "Note.md")));
        Assert.False(File.Exists(PathIn("Alpha", "Note.md")));
    }

    [Fact]
    public async Task Pull_tombstone_deletes_local_file()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Doomed", "x")], []));
        await _engine.PullAsync(false);
        Assert.True(File.Exists(PathIn("Doomed.md")));

        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1),
            [Doc("d1", "Doomed", "x", updated: T0.AddMinutes(1), deleted: T0.AddMinutes(1))], []));
        var report = await _engine.PullAsync(false);

        Assert.False(File.Exists(PathIn("Doomed.md")));
        Assert.Equal(1, report.Deleted);
    }

    [Fact]
    public async Task Pull_conflict_keeps_local_copy_and_takes_server()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Note", "v1")], []));
        await _engine.PullAsync(false);

        // Local edit between polls.
        await File.WriteAllTextAsync(PathIn("Note.md"), "local edit");

        // Server also changed.
        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1), [Doc("d1", "Note", "v2", updated: T0.AddMinutes(1))], []));
        var report = await _engine.PullAsync(false);

        Assert.Equal("v2", await File.ReadAllTextAsync(PathIn("Note.md")));
        Assert.Equal(1, report.Conflicts);
        var conflict = Directory.GetFiles(_root, "*.conflict-*.md").Single();
        Assert.Equal("local edit", await File.ReadAllTextAsync(conflict));
    }

    [Fact]
    public async Task Pull_unchanged_document_is_a_noop()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Note", "same")], []));
        await _engine.PullAsync(false);

        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(1), [Doc("d1", "Note", "same", updated: T0)], []));
        var report = await _engine.PullAsync(false);

        Assert.Equal(0, report.Pulled);
        Assert.Equal(0, report.Conflicts);
    }

    [Fact]
    public async Task FullSnapshot_reconcile_removes_documents_absent_from_server()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Keep", "a"), Doc("d2", "Gone", "b")], []));
        await _engine.PullAsync(false);
        Assert.True(File.Exists(PathIn("Gone.md")));

        // Server now only reports d1 as live.
        _client.FullSnapshot = new SyncDelta(T0.AddMinutes(5), [Doc("d1", "Keep", "a")], []);
        var report = await _engine.PullAsync(fullSnapshot: true);

        Assert.False(File.Exists(PathIn("Gone.md")));
        Assert.True(File.Exists(PathIn("Keep.md")));
        Assert.Equal(1, report.Deleted);
    }

    [Fact]
    public async Task LastSyncAt_cursor_is_persisted()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0.AddMinutes(3), [], []));
        await _engine.PullAsync(false);
        Assert.Equal(T0.AddMinutes(3), _state.GetLastSyncAt());
    }

    // ── Push: local → server ─────────────────────────────────────────────────────

    [Fact]
    public async Task Push_new_file_in_subfolders_creates_folder_chain_and_document()
    {
        var path = PathIn("Alpha", "Beta", "Note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "hello");

        await _engine.PushLocalChangeAsync(new LocalChange(LocalChangeKind.CreatedOrModified, path));

        Assert.Equal([("Alpha", (string?)null), ("Beta", "fld1")], _client.FoldersCreated);
        var created = Assert.Single(_client.Created);
        Assert.Equal("Note", created.Title);
        Assert.Equal("hello", created.Content);
        Assert.Equal("fld2", created.FolderId);
        Assert.NotNull(_state.GetDocumentByPath(path));
    }

    [Fact]
    public async Task Push_edit_updates_the_document()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Note", "original")], []));
        await _engine.PullAsync(false);

        await File.WriteAllTextAsync(PathIn("Note.md"), "changed");
        await _engine.PushLocalChangeAsync(new LocalChange(LocalChangeKind.CreatedOrModified, PathIn("Note.md")));

        var update = Assert.Single(_client.Updated);
        Assert.Equal("d1", update.Id);
        Assert.Equal("changed", update.Content);
    }

    [Fact]
    public async Task Push_echo_of_our_own_write_is_ignored()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Note", "same")], []));
        await _engine.PullAsync(false);

        // File content matches the recorded hash → nothing to push.
        await _engine.PushLocalChangeAsync(new LocalChange(LocalChangeKind.CreatedOrModified, PathIn("Note.md")));

        Assert.Empty(_client.Updated);
        Assert.Empty(_client.Created);
    }

    [Fact]
    public async Task Push_delete_removes_the_document()
    {
        _client.Deltas.Enqueue(new SyncDelta(T0, [Doc("d1", "Note", "x")], []));
        await _engine.PullAsync(false);

        File.Delete(PathIn("Note.md"));
        await _engine.PushLocalChangeAsync(new LocalChange(LocalChangeKind.Deleted, PathIn("Note.md")));

        Assert.Equal(["d1"], _client.DeletedIds);
        Assert.Null(_state.GetDocument("d1"));
    }

    [Fact]
    public async Task Push_ignores_conflict_files()
    {
        var conflict = PathIn("Note.conflict-20260801T120000.md");
        await File.WriteAllTextAsync(conflict, "junk");
        await _engine.PushLocalChangeAsync(new LocalChange(LocalChangeKind.CreatedOrModified, conflict));

        Assert.Empty(_client.Created);
        Assert.Empty(_client.Updated);
    }

    public void Dispose()
    {
        _state.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
