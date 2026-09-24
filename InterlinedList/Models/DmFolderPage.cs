namespace InterlinedList.Models;

/// <summary>
/// GET /api/dm?folder={inbox|sent|deleted} — one page of a personal DM folder,
/// newest first, returned BARE (no wrapper property).
///
/// Live-verified 2026-09-15 against the real API: the envelope is
/// <c>{"items":[…],"nextCursor":null}</c>, and a populated row carries
/// id / pairKey / senderId / recipientId / body / imageUrls / createdAt / readAt
/// plus nested <c>sender</c> and <c>recipient</c> objects and a plaintext
/// <c>preview</c> — see <see cref="DirectMessage"/>.
///
/// <see cref="NextCursor"/> is an opaque keyset cursor: pass it back verbatim as
/// <c>cursor</c>, never construct or parse one. It is null on the last page.
/// <c>take</c> is clamped server-side at 50.
/// </summary>
public sealed class DmFolderPage
{
    public List<DirectMessage> Items { get; init; } = new();
    public string? NextCursor { get; init; }
}
