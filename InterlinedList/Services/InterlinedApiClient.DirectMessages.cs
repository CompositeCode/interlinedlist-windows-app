using System.IO;
using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// 1:1 direct messages.
///
/// The product's real structure is three personal folders — Inbox / Sent /
/// Deleted — selected by the <c>folder</c> query parameter on
/// <c>GET /api/dm</c> (see <see cref="DmFolder"/>), plus
/// <c>GET /api/dm/conversations</c> for one row per conversation grouped by
/// pairKey, newest activity first. Both are cursor-paginated with an opaque
/// <c>nextCursor</c> that must be passed back verbatim. GET /api/dm/recipients
/// is no longer the conversation list — it is only the "who may I start a
/// conversation with" picker (the caller's mutual, approved followers).
///
/// NOTE on <see cref="SendDmAsync"/>: the request body
/// <c>{ recipientId, body, imageUrls? }</c> is now confirmed by the published
/// contract on /help/api/direct-messages, and the documented 201 response is
/// <c>{ "message": { … } }</c>. It is still deliberately NOT parsed here and
/// still NOT exercised live — sending would deliver a real message to a real
/// person's account — so callers stay on read-after-write and re-fetch the
/// thread afterward.
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>Server-side clamp on <c>take</c> for the cursor-paginated DM endpoints.</summary>
    public const int DmMaxTake = 50;

    /// <summary>
    /// One page of a personal DM folder, newest first
    /// (<c>GET /api/dm?folder=inbox|sent|deleted</c>).
    /// Live-verified 2026-09-15: 200 <c>{"items":[…],"nextCursor":null}</c> for
    /// all three values; any other folder value is a 400 "folder must be one of:
    /// inbox, sent, deleted.". Pass <paramref name="cursor"/> straight back from
    /// a previous response's <c>nextCursor</c> — never build one.
    /// </summary>
    public Task<DmFolderPage> GetDmFolderAsync(
        DmFolder folder, string? cursor = null, int take = 25, CancellationToken ct = default)
    {
        var path = $"api/dm?folder={folder.ToWireValue()}&take={Math.Clamp(take, 1, DmMaxTake)}";
        if (cursor is { Length: > 0 })
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        return GetJsonAsync<DmFolderPage>(path, ct);
    }

    /// <summary>
    /// One row per conversation, newest activity first
    /// (<c>GET /api/dm/conversations</c>). Envelope live-verified 2026-09-15;
    /// the row shape is only partially verified — see <see cref="DmConversation"/>.
    /// </summary>
    public Task<DmConversationsPage> GetDmConversationsAsync(
        string? cursor = null, int take = 25, CancellationToken ct = default)
    {
        var path = $"api/dm/conversations?take={Math.Clamp(take, 1, DmMaxTake)}";
        if (cursor is { Length: > 0 })
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        return GetJsonAsync<DmConversationsPage>(path, ct);
    }

    /// <summary>
    /// Re-fetch one message the caller participates in
    /// (<c>GET /api/dm/{id}</c> → <c>{ "message": { … } }</c>, live-verified
    /// 2026-09-15 against a real message id; an unknown id is a 404
    /// <c>{"error":"Message not found.","code":"not_found"}</c>). Used for
    /// read-after-write on a single row — e.g. picking up <c>readAt</c> after
    /// opening a thread marked it read — instead of reloading a whole folder.
    /// </summary>
    public async Task<DirectMessage> GetDmAsync(string id, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/dm/{Uri.EscapeDataString(id)}", ct);
        return json.TryGetProperty("message", out var message)
            && message.Deserialize<DirectMessage>(JsonOptions) is { } parsed
                ? parsed
                : throw new InterlinedApiException(200, "GET /api/dm/{id} returned no message.");
    }

    /// <summary>
    /// The people the caller is allowed to DM — their mutual, approved followers
    /// (<c>GET /api/dm/recipients</c> → <c>{ "recipients": [...] }</c>, verified
    /// live). This is the "New message" picker, not the conversation list.
    /// </summary>
    public async Task<List<DmRecipient>> GetDmRecipientsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/dm/recipients", ct);
        return json.TryGetProperty("recipients", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<DmRecipient>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// The conversation with one person, oldest → newest. Opening it marks the
    /// caller's received-unread messages in that conversation read, server-side
    /// (documented, and the reason callers refresh unread counts afterward).
    /// </summary>
    public Task<DmThread> GetDmThreadAsync(string username, CancellationToken ct = default)
        => GetJsonAsync<DmThread>($"api/dm/thread/{Uri.EscapeDataString(username)}", ct);

    public async Task<int> GetDmUnreadCountAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/dm/unread-count", ct);
        return json.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 0;
    }

    public Task SendDmAsync(string recipientId, string body, IReadOnlyList<string>? imageUrls = null, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/dm", new { recipientId, body, imageUrls }, ct);

    public Task MarkDmReadAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/dm/{id}/read", new { }, ct);

    /// <summary>
    /// Soft-delete only the caller's own side — moves the message into their
    /// Deleted folder. The other participant keeps their copy and can never tell
    /// (documented), and read state is untouched. Verified live 2026-09-15:
    /// 200 <c>{"ok":true}</c>.
    /// </summary>
    public Task TrashDmAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/dm/{id}/trash", new { }, ct);

    /// <summary>
    /// Clear the caller's own-side soft-delete — returns the message from Deleted
    /// to Inbox or Sent. Verified live 2026-09-15: 200 <c>{"ok":true}</c>.
    /// </summary>
    public Task RestoreDmAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/dm/{id}/restore", new { }, ct);

    /// <summary>Upload an image attachment for a DM (multipart field "file" → { url }, verified live).</summary>
    public async Task<string> UploadDmImageAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var json = await SendMultipartAsync("api/dm/images/upload", content, fileName, contentType, ct: ct);
        return json.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } u
            ? u
            : throw new InterlinedApiException(200, "DM image upload returned no url.");
    }

    /// <summary>Lightweight incremental fetch for polling an open thread ({ items }).</summary>
    public async Task<List<DirectMessage>> GetDmThreadUpdatesAsync(string username, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/dm/thread/{Uri.EscapeDataString(username)}/updates", ct);
        return json.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<DirectMessage>>(JsonOptions) ?? new()
            : new();
    }
}
