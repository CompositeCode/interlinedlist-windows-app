using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Walks a cursor-paginated collection endpoint one page at a time.
/// </summary>
/// <remarks>
/// <para>
/// The API is mid-migration between two paging models. <c>GET /api/messages</c>
/// now documents <c>cursor</c> keyset paging and returns <c>nextCursor</c> in the
/// pagination envelope; <c>offset</c> still works but is no longer a documented
/// parameter. Other endpoints (<c>GET /api/dm/conversations</c>) are
/// cursor-only.
/// </para>
/// <para>
/// This pager prefers the cursor and falls back to offset only when the response
/// gives no cursor, so a caller written against it works on both shapes and
/// keeps working as more endpoints move over.
/// </para>
/// <para>
/// <b>The cursor is opaque.</b> It is carried verbatim and never parsed — the
/// spec is explicit about that, and a "clever" client that decodes it will break
/// when the server changes its encoding.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class CursorPager<T>
{
    private readonly Func<CursorRequest, CancellationToken, Task<CursorPage<T>>> _fetch;

    public CursorPager(Func<CursorRequest, CancellationToken, Task<CursorPage<T>>> fetch)
        => _fetch = fetch;

    /// <summary>The opaque cursor for the next page, carried verbatim.</summary>
    public string? NextCursor { get; private set; }

    /// <summary>How many items have been loaded so far — the offset fallback.</summary>
    public int LoadedCount { get; private set; }

    /// <summary>
    /// Total item count the server reported. Only the first (offset) page sends
    /// one; cursor-fetched pages omit it, so this holds the first value seen.
    /// </summary>
    public int? Total { get; private set; }

    /// <summary>
    /// False once the server says there is nothing more. Starts true so the
    /// first <see cref="NextPageAsync"/> always runs.
    /// </summary>
    public bool HasMore { get; private set; } = true;

    /// <summary>True before the first page has been fetched.</summary>
    public bool IsFirstPage => LoadedCount == 0 && NextCursor is null;

    /// <summary>
    /// Fetches the next page. Returns an empty list when exhausted, so a caller
    /// can loop without checking <see cref="HasMore"/> first.
    /// </summary>
    public async Task<IReadOnlyList<T>> NextPageAsync(int limit, CancellationToken ct = default)
    {
        if (!HasMore) return [];

        var page = await _fetch(new CursorRequest(limit, NextCursor, LoadedCount), ct);

        LoadedCount += page.Items.Count;
        NextCursor = page.Pagination?.NextCursor;

        // Verified live 2026-09-16: a cursor-fetched page's envelope carries only
        // { limit, hasMore, nextCursor } — `total` and `offset` are absent, so
        // they deserialize to 0. Keep the total from the first page instead of
        // letting a later page zero it out.
        if (page.Pagination?.Total is > 0) Total = page.Pagination.Total;

        // Trust hasMore when the server sends it; otherwise a short page is the
        // end. A cursor-less response with a full page falls back to offset.
        HasMore = page.Pagination?.HasMore ?? (page.Items.Count >= limit && limit > 0);
        if (page.Items.Count == 0) HasMore = false;

        return page.Items;
    }

    /// <summary>Rewinds to the beginning — for a pull-to-refresh or a filter change.</summary>
    public void Reset()
    {
        NextCursor = null;
        LoadedCount = 0;
        Total = null;
        HasMore = true;
    }
}

/// <summary>
/// What the pager asks for. Use <see cref="Cursor"/> when it is non-null and
/// the endpoint supports it; otherwise fall back to <see cref="Offset"/>.
/// </summary>
/// <param name="Limit">Page size.</param>
/// <param name="Cursor">Opaque cursor from the previous page — pass verbatim, never parse.</param>
/// <param name="Offset">Items already loaded, for endpoints with no cursor.</param>
public readonly record struct CursorRequest(int Limit, string? Cursor, int Offset)
{
    /// <summary>
    /// The query fragment for this request — <c>limit=20&amp;cursor=…</c> when a
    /// cursor is available, else <c>limit=20&amp;offset=40</c>. Values are
    /// URL-escaped.
    /// </summary>
    public string ToQuery() => Cursor is { Length: > 0 } cursor
        ? $"limit={Limit}&cursor={Uri.EscapeDataString(cursor)}"
        : $"limit={Limit}&offset={Offset}";
}

/// <summary>One page of a cursor-paginated collection.</summary>
/// <param name="Items">The page's items.</param>
/// <param name="Pagination">The envelope, when the endpoint sends one.</param>
public readonly record struct CursorPage<T>(IReadOnlyList<T> Items, Pagination? Pagination);
