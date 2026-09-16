namespace InterlinedList.Models;

/// <summary>
/// One row from <c>GET /api/tags/trending</c> or <c>GET /api/tags/autocomplete</c>.
/// Both wrap their rows under <c>{ "tags": [ … ] }</c>; autocomplete omits
/// <see cref="LastUsedAt"/>. Verified live 2026-09-16.
/// </summary>
/// <remarks>
/// The two endpoints' <see cref="Count"/> values are <b>not</b> the same number:
/// trending counts uses within its trailing window, autocomplete counts all-time
/// (live example: <c>thoughts</c> was 1 in trending and 5 in autocomplete). Don't
/// treat one as a cache of the other.
/// </remarks>
public sealed class TrendingTag
{
    public required string Tag { get; init; }

    /// <summary>How many messages carry the tag — scoped differently per endpoint (see remarks).</summary>
    public int Count { get; init; }

    /// <summary>Trending only; null from autocomplete.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>Display form for a chip.</summary>
    public string Label => "#" + Tag;

    /// <summary>
    /// Tooltip text. Built here rather than via a binding <c>StringFormat</c>
    /// because the target (<c>ToolTip</c>) is object-typed, where StringFormat
    /// isn't reliably applied.
    /// </summary>
    public string CountLabel => Count == 1 ? "1 message" : $"{Count} messages";
}
