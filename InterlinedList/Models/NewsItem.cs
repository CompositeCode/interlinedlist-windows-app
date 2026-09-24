namespace InterlinedList.Models;

/// <summary>
/// One headline from GET /api/widgets/news. Every field was present and
/// non-null on all six observed rows (key union taken across the whole array,
/// not just row zero), but they stay nullable because the upstream is a
/// third-party proxy this client cannot constrain.
/// </summary>
public sealed class NewsItem
{
    public string? Title { get; init; }
    public string? Url { get; init; }
    public int? Points { get; init; }
}
