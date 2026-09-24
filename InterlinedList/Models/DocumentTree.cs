namespace InterlinedList.Models;

/// <summary>
/// The whole Documents sidebar in one payload: <c>GET /api/documents/tree</c>
/// (the spec calls it the "Combined sidebar tree payload"). Folders arrive with
/// their direct documents already embedded and carry <c>ParentId</c> for nesting;
/// <see cref="RootDocuments"/> holds the documents that sit outside any folder.
///
/// Verified live 2026-09-15: the union of keys across ALL folder rows is
/// { id, name, parentId, documents } and across ALL document nodes (both folder
/// children and root documents) is { id, title, relativePath, isPublic } — so
/// every document node is a <see cref="DocumentFolderEntry"/>.
///
/// Deliberately a LIGHTWEIGHT projection: the document nodes carry no
/// <c>content</c>/<c>updatedAt</c>/<c>version</c>, unlike the rows from
/// <c>GET /api/documents</c>. Callers that need a document's body fetch it on
/// demand with <c>GetDocumentAsync(id)</c>; there is no query parameter that
/// makes the tree include content (<c>?includeContent=true</c> and
/// <c>?content=true</c> are both ignored — probed live 2026-09-15).
/// </summary>
public sealed class DocumentTree
{
    public required List<DocumentFolder> Folders { get; init; }
    public required List<DocumentFolderEntry> RootDocuments { get; init; }
}
