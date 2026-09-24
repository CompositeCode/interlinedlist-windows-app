using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Tags. Two read endpoints, both answering the bearer sync-token and both
/// wrapping their payload under <c>{ "tags": [ … ] }</c> — verified live
/// 2026-09-16 against the test account.
/// </summary>
/// <remarks>
/// Writing tags is not here: they ride along on <c>POST /api/messages</c> as
/// <c>tags: string[]</c>, so that lives on <see cref="NewMessage"/>. Filtering
/// the feed by tag is <c>GET /api/messages?tag=</c> — see
/// <c>GetFeedPageAsync</c> in InterlinedApiClient.Messages.cs.
/// </remarks>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Most-used tags on public messages in a trailing window. Honors
    /// <paramref name="limit"/>; returns 20 by default.
    /// </summary>
    public Task<List<TrendingTag>> GetTrendingTagsAsync(int limit = 20, CancellationToken ct = default)
        => ReadTagsAsync($"api/tags/trending?limit={limit}", ct);

    /// <summary>
    /// Case-insensitive <b>literal prefix</b> match — not a substring search.
    /// Live: <c>q=le</c> → <c>Lego</c>, <c>learning</c>; <c>q=ego</c> → nothing,
    /// even though <c>Lego</c> contains it.
    /// </summary>
    /// <remarks>
    /// An empty <c>q</c> is a <c>400 {"error":"Query parameter 'q' is required"}</c>,
    /// so a blank query short-circuits to an empty list rather than round-tripping
    /// into an error the caller would have to swallow.
    /// </remarks>
    public Task<List<TrendingTag>> AutocompleteTagsAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new List<TrendingTag>());

        return ReadTagsAsync($"api/tags/autocomplete?q={Uri.EscapeDataString(query.Trim())}&limit={limit}", ct);
    }

    private async Task<List<TrendingTag>> ReadTagsAsync(string path, CancellationToken ct)
    {
        var json = await GetElementAsync(path, ct);
        return json.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<TrendingTag>>(JsonOptions) ?? new()
            : new();
    }
}
