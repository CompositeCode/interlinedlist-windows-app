namespace InterlinedList.Sync.Core;

/// <summary>A document as it exists on the server (wire shape, engine-facing).</summary>
public sealed record RemoteDocument(
    string Id,
    string Title,
    string? Content,
    string? FolderId,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt)
{
    /// <summary>True when this delta entry is a tombstone (document removed server-side).</summary>
    public bool IsDeleted => DeletedAt is not null;
}

/// <summary>A folder as it exists on the server (flat wire shape; tree is derived).</summary>
public sealed record RemoteFolder(string Id, string Name, string? ParentId);

/// <summary>
/// The result of a pull — either an incremental delta (documents/folders changed
/// since <c>lastSyncAt</c>) or a full snapshot (used for the periodic
/// deletion-reconciliation pass, since the API's delta tombstones are unreliable).
/// </summary>
public sealed record SyncDelta(
    DateTimeOffset? LastSyncAt,
    IReadOnlyList<RemoteDocument> Documents,
    IReadOnlyList<RemoteFolder> Folders);
