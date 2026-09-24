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

    // ── Push (repost) / Quote ───────────────────────────────────────────────────

    /// <summary>
    /// <c>POST /api/messages</c> from a <see cref="NewMessage"/>. An overload of the
    /// long positional <c>PostMessageAsync</c> in InterlinedApiClient.cs that can
    /// also send <c>pushedMessageId</c>. Optional keys the caller didn't set are
    /// omitted rather than sent as explicit nulls.
    /// </summary>
    public Task PostMessageAsync(NewMessage draft, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = draft.Content,
            ["publiclyVisible"] = draft.PubliclyVisible,
            ["crossPostToBluesky"] = draft.CrossPostToBluesky,
            ["crossPostToTwitter"] = draft.CrossPostToTwitter,
            ["crossPostToLinkedIn"] = draft.CrossPostToLinkedIn,
        };
        if (draft.MastodonProviderIds is { Length: > 0 } mastodon) body["mastodonProviderIds"] = mastodon;
        if (draft.ParentId is { Length: > 0 } parent) body["parentId"] = parent;
        if (draft.PushedMessageId is { Length: > 0 } pushed) body["pushedMessageId"] = pushed;
        if (draft.ScheduledAt is { } at) body["scheduledAt"] = at.UtcDateTime;
        if (draft.ImageUrls is { Count: > 0 } images) body["imageUrls"] = images;
        if (draft.VideoUrls is { Count: > 0 } videos) body["videoUrls"] = videos;

        return SendVoidAsync(HttpMethod.Post, "api/messages", body, ct);
    }

    /// <summary>
    /// Push (repost) a message as-is, with no commentary. Always public — pushes
    /// and quotes are public by product rule (<c>/help/messages</c>), regardless of
    /// the user's <c>defaultPubliclyVisible</c> preference.
    /// </summary>
    public Task PushMessageAsync(string messageId, CancellationToken ct = default)
        => PostMessageAsync(new NewMessage { Content = "", PubliclyVisible = true, PushedMessageId = messageId }, ct);

    /// <summary>
    /// Quote a message — a push carrying your own note. Same endpoint and same
    /// <c>pushedMessageId</c> field as <see cref="PushMessageAsync"/>; non-empty
    /// content is the only thing that distinguishes the two. Always public.
    /// </summary>
    public Task QuoteMessageAsync(string messageId, string content, CancellationToken ct = default)
        => PostMessageAsync(new NewMessage { Content = content, PubliclyVisible = true, PushedMessageId = messageId }, ct);

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

    /// <summary>Upload a video; same multipart shape as image upload (field "file" → { url }).</summary>
    public async Task<string> UploadMessageVideoAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var json = await SendMultipartAsync("api/messages/videos/upload", content, fileName, contentType, ct: ct);
        return json.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } u
            ? u
            : throw new InterlinedApiException(200, "Video upload returned no url.");
    }

    /// <summary>GET /api/messages/scheduled → the current user's not-yet-published posts ({ messages }).</summary>
    public async Task<List<Message>> GetScheduledMessagesAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/messages/scheduled", ct);
        return json.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<Message>>(JsonOptions) ?? new()
            : new();
    }
}
