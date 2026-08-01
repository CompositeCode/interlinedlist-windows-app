using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Sync.Core;

/// <summary>Thrown when there is no bearer token (user is signed out).</summary>
public sealed class NotSignedInException() : Exception("No InterlinedList sync token is available.");

/// <summary>Thrown when the server rejects the bearer token (401) — sign-in expired.</summary>
public sealed class AuthExpiredException() : Exception("The InterlinedList sync token was rejected (401).");

/// <summary>Thrown on HTTP 429; carries the server's Retry-After (seconds) when present.</summary>
public sealed class RateLimitedException(int? retryAfterSeconds)
    : Exception("Rate limited by the InterlinedList API (429).")
{
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Talks to the InterlinedList documents API using the shared bearer sync-token.
/// Deliberately re-implements only the ~7 endpoints the sync engine needs (rather
/// than referencing the WPF app), so <c>Sync.Core</c> stays platform-neutral.
/// </summary>
public sealed class HttpDocumentSyncClient(HttpClient http, ICredentialSource credentials, string baseUrl = "https://interlinedlist.com")
    : IDocumentSyncClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _base = baseUrl.TrimEnd('/');

    public Task<SyncDelta> GetDeltaAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var url = $"{_base}/api/documents/sync";
        if (since is { } s) url += "?lastSyncAt=" + Uri.EscapeDataString(s.ToString("O"));
        return FetchDeltaAsync(url, ct);
    }

    // No cursor ⇒ the API returns every live document (with content) — authoritative
    // for deletion reconciliation.
    public Task<SyncDelta> GetFullSnapshotAsync(CancellationToken ct) =>
        FetchDeltaAsync($"{_base}/api/documents/sync", ct);

    private async Task<SyncDelta> FetchDeltaAsync(string url, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, url, null, ct);
        var dto = await ReadAsync<SyncDeltaDto>(resp, ct);
        return new SyncDelta(
            dto.LastSyncAt,
            (dto.Documents ?? []).Select(d => d.ToModel()).ToList(),
            (dto.Folders ?? []).Select(f => new RemoteFolder(f.Id, f.Name ?? string.Empty, f.ParentId)).ToList());
    }

    public async Task<RemoteDocument> CreateDocumentAsync(string title, string content, string? folderId, CancellationToken ct)
    {
        var url = folderId is null
            ? $"{_base}/api/documents"
            : $"{_base}/api/documents/folders/{Uri.EscapeDataString(folderId)}/documents";
        using var resp = await SendAsync(HttpMethod.Post, url, new { title, content }, ct);
        return (await ReadAsync<DocumentEnvelope>(resp, ct)).Require().ToModel();
    }

    public async Task<RemoteDocument> UpdateDocumentAsync(string id, string title, string content, string? folderId, CancellationToken ct)
    {
        // NOTE: folderId on PATCH is not yet live-verified against the API — moves may
        // need a dedicated endpoint. Sent optimistically; see synch-plan.md risks.
        var url = $"{_base}/api/documents/{Uri.EscapeDataString(id)}";
        using var resp = await SendAsync(HttpMethod.Patch, url, new { title, content, folderId }, ct);
        return (await ReadAsync<DocumentEnvelope>(resp, ct)).Require().ToModel();
    }

    public async Task DeleteDocumentAsync(string id, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"{_base}/api/documents/{Uri.EscapeDataString(id)}", null, ct);
        // 404 is treated as success (already gone).
        if (resp.StatusCode is not (HttpStatusCode.NotFound) && !resp.IsSuccessStatusCode)
            Throw(resp);
    }

    public async Task<RemoteFolder> CreateFolderAsync(string name, string? parentId, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Post, $"{_base}/api/documents/folders", new { name, parentId }, ct);
        var env = await ReadAsync<FolderEnvelope>(resp, ct);
        var f = env.Folder ?? new FolderDto { Id = env.Id ?? string.Empty, Name = name, ParentId = parentId };
        return new RemoteFolder(f.Id, f.Name ?? name, f.ParentId ?? parentId);
    }

    // ── Transport ────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var token = credentials.GetToken() ?? throw new NotSignedInException();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);

        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.Unauthorized) { resp.Dispose(); throw new AuthExpiredException(); }
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = (int?)resp.Headers.RetryAfter?.Delta?.TotalSeconds;
            resp.Dispose();
            throw new RateLimitedException(retry);
        }
        return resp;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode) Throw(resp);
        return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))
               ?? throw new HttpRequestException($"Empty response body from {resp.RequestMessage?.RequestUri}");
    }

    private static void Throw(HttpResponseMessage resp) =>
        throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} from {resp.RequestMessage?.RequestUri}");

    // ── Wire DTOs ────────────────────────────────────────────────────────────────

    private sealed class SyncDeltaDto
    {
        [JsonPropertyName("lastSyncAt")] public DateTimeOffset? LastSyncAt { get; set; }
        [JsonPropertyName("documents")] public List<DocumentDto>? Documents { get; set; }
        [JsonPropertyName("folders")] public List<FolderDto>? Folders { get; set; }
    }

    private sealed class DocumentDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("folderId")] public string? FolderId { get; set; }
        [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("deletedAt")] public DateTimeOffset? DeletedAt { get; set; }

        public RemoteDocument ToModel() => new(Id, Title ?? string.Empty, Content, FolderId, UpdatedAt, DeletedAt);
    }

    private sealed class FolderDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("parentId")] public string? ParentId { get; set; }
    }

    private sealed class DocumentEnvelope
    {
        [JsonPropertyName("document")] public DocumentDto? Document { get; set; }
        // Some write endpoints return the document at the root instead of wrapped.
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("folderId")] public string? FolderId { get; set; }
        [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; set; }

        public DocumentDto Require() => Document ?? new DocumentDto
        {
            Id = Id ?? string.Empty,
            Title = Title,
            Content = Content,
            FolderId = FolderId,
            UpdatedAt = UpdatedAt ?? DateTimeOffset.UtcNow,
        };
    }

    private sealed class FolderEnvelope
    {
        [JsonPropertyName("folder")] public FolderDto? Folder { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
