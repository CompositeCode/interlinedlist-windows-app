namespace InterlinedList.Models;

/// <summary>
/// GET /api/dm/conversations — cursor-paginated conversation list, newest
/// activity first. The envelope <c>{"items":[…],"nextCursor":null}</c> is
/// live-verified (2026-09-15); the row shape is only partially verified — see
/// <see cref="DmConversation"/>.
///
/// <see cref="NextCursor"/> is opaque: pass it back verbatim as <c>cursor</c>.
/// </summary>
public sealed class DmConversationsPage
{
    public List<DmConversation> Items { get; init; } = new();
    public string? NextCursor { get; init; }
}
