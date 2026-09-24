namespace InterlinedList.Models;

/// <summary>
/// The lightweight document node the API returns inside folder listings and in
/// the sidebar tree — id/title/relativePath/isPublic and nothing else.
///
/// Shared by three shapes, all live-verified 2026-09-15 to carry exactly these
/// four keys: <c>GET /api/documents/folders</c> → <c>folders[*].documents[*]</c>,
/// <c>GET /api/documents/tree</c> → <c>folders[*].documents[*]</c>, and
/// <c>GET /api/documents/tree</c> → <c>rootDocuments[*]</c>.
///
/// <c>RelativePath</c> is the server's own path-within-the-vault for the document
/// (e.g. <c>"a-single-doc.md"</c>) — the same concept the sync engine's
/// <c>FileMapper</c> materializes on disk.
/// </summary>
public sealed class DocumentFolderEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public bool IsPublic { get; init; }
}
