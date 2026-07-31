using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Thin wrapper over https://interlinedlist.com/api. Auth is a long-lived
/// bearer token minted by POST /api/auth/sync-token (the same mechanism the
/// il-sync CLI and other native clients use) — no cookie jar needed.
/// Split across partial-class files by domain: this file holds auth/user/
/// messages/notifications; Lists/Documents/Organizations/Search/CrossPost
/// live in InterlinedApiClient.{Domain}.cs alongside it.
/// </summary>
public sealed partial class InterlinedApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public string? AccessToken { get; set; }

    public InterlinedApiClient(HttpClient httpClient)
    {
        _http = httpClient;
        _http.BaseAddress ??= new Uri(ApiConfig.BaseUrl);
    }

    public async Task<string> RequestSyncTokenAsync(string email, string password, string deviceLabel, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/auth/sync-token",
            new { email, password, deviceLabel, name = deviceLabel }, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.TryGetProperty("token", out var tokenProp) && tokenProp.GetString() is { Length: > 0 } token
            ? token
            : throw new InterlinedApiException((int)resp.StatusCode, "Sync-token response did not include a token.");
    }

    public async Task<CurrentUser> GetCurrentUserAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/user", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("user").Deserialize<CurrentUser>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/user returned no user.");
    }

    public async Task<MessagesPage> GetMessagesAsync(int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/messages?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<MessagesPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/messages returned no body.");
    }

    public async Task PostMessageAsync(
        string content,
        bool publiclyVisible,
        bool crossPostToBluesky = false,
        bool crossPostToTwitter = false,
        bool crossPostToLinkedIn = false,
        string? mastodonProviderIds = null,
        string? parentId = null,
        DateTimeOffset? scheduledAt = null,
        IReadOnlyList<string>? imageUrls = null,
        CancellationToken ct = default)
    {
        // parentId turns this into a reply; scheduledAt defers publication;
        // imageUrls attaches already-uploaded images. All are documented request
        // fields on POST /api/messages (OpenAPI-verified).
        using var resp = await SendAsync(HttpMethod.Post, "api/messages", new
        {
            content,
            publiclyVisible,
            crossPostToBluesky,
            crossPostToTwitter,
            crossPostToLinkedIn,
            mastodonProviderIds,
            parentId,
            scheduledAt = scheduledAt?.UtcDateTime,
            imageUrls
        }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task DigAsync(string messageId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, $"api/messages/{messageId}/dig", new { }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task UndigAsync(string messageId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"api/messages/{messageId}/dig", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<NotificationsPage> GetNotificationsAsync(int limit = 20, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/notifications?scope=tray&limit={limit}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<NotificationsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/notifications returned no body.");
    }

    public async Task MarkAllNotificationsReadAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/notifications/mark-all-read", new { }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public Task MarkNotificationReadAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Patch, $"api/notifications/{id}/read", new { }, ct);

    public Task DeleteNotificationAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/notifications/{id}", null, ct);

    public async Task<FollowCounts> GetFollowCountsAsync(string userId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/follow/{userId}/counts", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<FollowCounts>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/follow/{id}/counts returned no body.");
    }

    // ── Shared JSON plumbing (Phase 0) ──────────────────────────────────────────
    // New domain partials build on these instead of re-hand-rolling the
    // SendAsync → EnsureSuccess → ReadFromJson dance. Two write helpers exist by
    // design: SendJsonAsync<T> for live-verified response envelopes, and
    // SendVoidAsync for the codebase's read-after-write pattern (mutations whose
    // body shape isn't trusted — the caller re-fetches from a GET afterward).

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, $"GET {path} returned no body.");
    }

    /// <summary>Read an endpoint that wraps its payload under a property (e.g. { "lists": [...] }).</summary>
    private async Task<JsonElement> GetElementAsync(string path, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    /// <summary>Raw text body — used for CSV export endpoints.</summary>
    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// multipart/form-data upload. The field name is "file" and the response is
    /// { "url": "..." } for the image endpoints (verified live 2026-07-31).
    /// Returns the root JSON element so callers can pull whatever key they need.
    /// </summary>
    private async Task<JsonElement> SendMultipartAsync(
        string path, Stream content, string fileName, string contentType,
        string fieldName = "file", CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, fieldName, fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        if (AccessToken is { Length: > 0 })
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        using var resp = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    /// <summary>Mutating call whose response body we don't parse (read-after-write pattern).</summary>
    private async Task SendVoidAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>Mutating call whose response envelope IS live-verified and deserialized.</summary>
    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, $"{method} {path} returned no body.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (AccessToken is { Length: > 0 })
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        return await _http.SendAsync(request, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
                message = errorProp.GetString() ?? body;
        }
        catch (JsonException)
        {
            // Body wasn't JSON — surface the raw text.
        }

        throw new InterlinedApiException((int)resp.StatusCode, message);
    }
}
