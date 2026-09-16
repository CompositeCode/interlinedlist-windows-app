using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Link unfurling. The feed rarely needs any of this: <c>linkMetadata</c> arrives
/// <b>inline</b> on each message in <c>GET /api/messages</c> (42 of 80 rows in a
/// live sample), so preview cards render with no extra call. These are for the
/// compose-time preview and for re-reading a single message's stored metadata.
/// </summary>
/// <remarks>
/// <para>
/// The two shapes differ, which is easy to get wrong: the ad-hoc endpoint wraps
/// one entry as <c>{ "link": { … } }</c>, while the per-message endpoint (and the
/// inline field) wrap an array as <c>{ "links": [ … ] }</c>. Verified live
/// 2026-09-16.
/// </para>
/// <para>
/// A failed unfurl is <b>not</b> an HTTP error: an unreachable host still answers
/// <c>200</c> with <c>{ "link": { url, platform, fetchStatus: "failed" } }</c> and
/// no <c>metadata</c>. Callers must check, which is what
/// <c>LinkMetadataExtensions.IsRenderable</c> is for. A missing <c>url</c>
/// parameter <i>is</i> a <c>400</c>.
/// </para>
/// </remarks>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Unfurl an arbitrary URL — <c>GET /api/link-metadata?url=</c>. Returns null
    /// when the fetch failed or produced no metadata, so a caller can treat
    /// "nothing to show" uniformly rather than inspecting <c>fetchStatus</c>.
    /// </summary>
    public async Task<LinkPreview?> GetLinkMetadataAsync(string url, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/link-metadata?url={Uri.EscapeDataString(url)}", ct);
        if (!json.TryGetProperty("link", out var link) || link.ValueKind != JsonValueKind.Object)
            return null;

        var preview = link.Deserialize<LinkPreview>(JsonOptions);
        return preview.IsRenderable() ? preview : null;
    }

    /// <summary>
    /// A message's stored metadata — <c>GET /api/messages/{id}/metadata</c>.
    /// Lightweight (no re-fetch server-side); only the renderable entries come
    /// back. Rarely needed, since the same data is inline on the feed.
    /// </summary>
    public async Task<IReadOnlyList<LinkPreview>> GetMessageLinkMetadataAsync(string messageId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/messages/{messageId}/metadata", ct);
        return json.Deserialize<LinkMetadataEnvelope>(JsonOptions).SuccessfulLinks();
    }
}
