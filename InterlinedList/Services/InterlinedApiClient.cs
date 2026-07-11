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
        string? mastodonProviderIds = null,
        CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/messages", new
        {
            content,
            publiclyVisible,
            crossPostToBluesky,
            crossPostToTwitter,
            mastodonProviderIds
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

    public async Task<FollowCounts> GetFollowCountsAsync(string userId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/follow/{userId}/counts", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<FollowCounts>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/follow/{id}/counts returned no body.");
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
