using System.IO;
using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Message depth beyond the feed: single-message detail, replies/threads,
/// and edit/delete/report of a post. Replies are just POST /api/messages with a
/// parentId (see <see cref="PostMessageAsync"/>). Edit/delete/report response
/// envelopes aren't parsed — callers re-fetch (the codebase's read-after-write
/// pattern) rather than trusting an unverified write body.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<Message> GetMessageAsync(string id, CancellationToken ct = default)
    {
        // Detail is served either bare or wrapped as { "message": {...} } — accept both.
        var root = await GetElementAsync($"api/messages/{id}", ct);
        var el = root.TryGetProperty("message", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object
            ? wrapped
            : root;
        return el.Deserialize<Message>(JsonOptions)
            ?? throw new InterlinedApiException(200, "GET /api/messages/{id} returned no message.");
    }

    public async Task<List<Message>> GetRepliesAsync(string id, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/messages/{id}/replies", ct);
        return json.TryGetProperty("replies", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<Message>>(JsonOptions) ?? new()
            : new();
    }

    public Task PostReplyAsync(string parentId, string content, bool publiclyVisible, CancellationToken ct = default)
        => PostMessageAsync(content, publiclyVisible, parentId: parentId, ct: ct);

    public Task EditMessageAsync(string id, string content, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Patch, $"api/messages/{id}", new { content }, ct);

    public Task DeleteMessageAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/messages/{id}", null, ct);

    public Task ReportMessageAsync(string id, string reason, string? detail, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/messages/{id}/report", new { reason, detail }, ct);

    /// <summary>
    /// Upload an image for a post; returns the hosted URL to pass back in the
    /// message's imageUrls. multipart field "file", response { url } — both
    /// verified live 2026-07-31. Subscriber-gated (402/403 for free accounts).
    /// </summary>
    public async Task<string> UploadMessageImageAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var json = await SendMultipartAsync("api/messages/images/upload", content, fileName, contentType, ct: ct);
        return json.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } u
            ? u
            : throw new InterlinedApiException(200, "Image upload returned no url.");
    }
}
