namespace InterlinedList.Models;

/// <summary>
/// The pagination envelope the API returns alongside a collection.
/// </summary>
/// <remarks>
/// Two paging models coexist. <see cref="Offset"/> still works on the endpoints
/// that historically accepted it, but keyset paging via <see cref="NextCursor"/>
/// is the documented path now (<c>GET /api/messages</c> documents
/// <c>limit</c>/<c>onlyMine</c>/<c>tag</c>/<c>cursor</c> and no longer lists
/// <c>offset</c>). Prefer the cursor where the response offers one.
/// <para>
/// <see cref="NextCursor"/> is <b>opaque</b>. The spec is explicit: pass it back
/// verbatim — never construct, parse, or modify one.
/// </para>
/// </remarks>
public sealed class Pagination
{
    /// <summary>
    /// Total matching items. <b>Only sent on an offset-paged response</b> —
    /// verified live 2026-09-16, a cursor-fetched page's envelope carries only
    /// <c>limit</c>, <c>hasMore</c> and <c>nextCursor</c>, so this reads 0 there.
    /// Don't treat 0 as "no results".
    /// </summary>
    public int Total { get; init; }

    public int Limit { get; init; }

    /// <summary>Absent (0) on a cursor-fetched page — see <see cref="Total"/>.</summary>
    public int Offset { get; init; }

    public bool HasMore { get; init; }

    /// <summary>
    /// Opaque keyset cursor for the next page, or null on the last page / on
    /// endpoints that don't offer one. Pass back verbatim.
    /// </summary>
    public string? NextCursor { get; init; }
}
