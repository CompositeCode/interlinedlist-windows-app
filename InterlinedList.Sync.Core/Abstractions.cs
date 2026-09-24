namespace InterlinedList.Sync.Core;

/// <summary>
/// Supplies the InterlinedList bearer sync-token. The Windows tray implementation
/// reads the SAME DPAPI-encrypted <c>%LocalAppData%\InterlinedList\session.dat</c>
/// the main app writes, so the user signs in once. Returns null when signed out.
/// </summary>
public interface ICredentialSource
{
    string? GetToken();
}

/// <summary>The documents/folders API surface the engine needs (server seam).</summary>
public interface IDocumentSyncClient
{
    /// <summary>Incremental delta since <paramref name="since"/> (null = everything).</summary>
    Task<SyncDelta> GetDeltaAsync(DateTimeOffset? since, CancellationToken ct);

    /// <summary>Authoritative full snapshot (all live documents + folders) for reconcile.</summary>
    Task<SyncDelta> GetFullSnapshotAsync(CancellationToken ct);

    Task<RemoteDocument> CreateDocumentAsync(string title, string content, string? folderId, CancellationToken ct);
    Task<RemoteDocument> UpdateDocumentAsync(string id, string title, string content, string? folderId, CancellationToken ct);
    Task DeleteDocumentAsync(string id, CancellationToken ct);

    Task<RemoteFolder> CreateFolderAsync(string name, string? parentId, CancellationToken ct);
}

/// <summary>Persisted mapping of a server document to its local file + last-synced state.</summary>
public sealed record DocumentRecord(
    string Id,
    string LocalPath,
    string? FolderId,
    DateTimeOffset ServerUpdatedAt,
    string ContentHash);

/// <summary>Persisted mapping of a server folder to its local (relative) directory.</summary>
public sealed record FolderRecord(
    string Id,
    string? ParentId,
    string Name,
    string RelativePath);

/// <summary>
/// Local persistence of the sync ledger (SQLite <c>state.db</c>): which server
/// document/folder lives at which local path, and the last delta cursor.
/// </summary>
public interface ISyncStateStore : IDisposable
{
    // Documents
    DocumentRecord? GetDocument(string id);
    DocumentRecord? GetDocumentByPath(string localPath);
    IReadOnlyList<DocumentRecord> AllDocuments();
    void UpsertDocument(DocumentRecord record);
    void DeleteDocument(string id);

    // Folders
    FolderRecord? GetFolder(string id);
    FolderRecord? GetFolderByRelativePath(string relativePath);
    IReadOnlyList<FolderRecord> AllFolders();
    void UpsertFolder(FolderRecord record);
    void DeleteFolder(string id);

    // Cursor
    DateTimeOffset? GetLastSyncAt();
    void SetLastSyncAt(DateTimeOffset value);
}

/// <summary>The four outcomes of comparing local vs. remote state for one document.</summary>
public enum SyncAction
{
    /// <summary>Neither side changed — nothing to do.</summary>
    NoOp,
    /// <summary>Only the local file changed — push it to the server.</summary>
    Push,
    /// <summary>Only the server changed — pull it to disk.</summary>
    Pull,
    /// <summary>Both changed — keep a local conflict copy, then take the server version.</summary>
    ConflictCopyThenPull,
}
