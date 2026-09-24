namespace InterlinedList.Models;

/// <summary>
/// GET /api/widgets/news — top Hacker News headlines.
/// Live-verified 2026-09-16: 200 { "items": [ { "title", "url", "points" } ] }
/// (6 items). The documented <c>source</c> query parameter is accepted but had
/// no observable effect — "hackernews", "hn", "top", "best", "new" and even a
/// nonsense value all returned the byte-identical default list, so the server
/// either ignores it or silently falls back.
/// </summary>
public sealed class NewsWidget
{
    public List<NewsItem> Items { get; init; } = new();
}
