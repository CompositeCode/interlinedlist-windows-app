namespace InterlinedList.Models;

/// <summary>
/// A list folder, from <c>GET /api/folders</c>.
/// </summary>
/// <remarks>
/// <para>
/// A <b>separate resource</b> from document folders (<c>/api/documents/folders</c>)
/// despite the similar shape — different endpoints, different ids, no overlap.
/// </para>
/// <para>
/// Live shape verified 2026-09-16 by creating a parent and a child on a
/// throwaway (both since deleted): the payload is exactly
/// <c>{id, name, parentId}</c>, flat, with nesting expressed only through
/// <c>parentId</c>. There is no <c>createdAt</c>/<c>updatedAt</c>/<c>userId</c>
/// and no server-side nesting.
/// </para>
/// </remarks>
public sealed class ListFolder
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Parent folder, or null at root. The only nesting signal.</summary>
    public string? ParentId { get; init; }

    public bool IsRoot => ParentId is null or "";
}
