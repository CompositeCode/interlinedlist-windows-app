using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Personal markdown documents: root document list, flat folders (each embedding
/// its direct documents), and templates. PATCH/from-template write responses
/// weren't fully verified live, so those paths just check success and the
/// caller re-fetches instead of trusting a parsed body.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<DocumentsPage> GetRootDocumentsAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/documents", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DocumentsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/documents returned no body.");
    }

    public async Task<DocumentFoldersPage> GetDocumentFoldersAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/documents/folders", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DocumentFoldersPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/documents/folders returned no body.");
    }

    public async Task<DocumentTemplatesResponse> GetDocumentTemplatesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/documents/templates", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DocumentTemplatesResponse>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/documents/templates returned no body.");
    }

    public async Task<DocumentSummary> GetDocumentAsync(string id, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/documents/{id}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("document").Deserialize<DocumentSummary>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/documents/{id} returned no document.");
    }

    public async Task<DocumentSummary> CreateDocumentAsync(string title, string content, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/documents", new { title, content, isPublic = false }, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("document").Deserialize<DocumentSummary>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "POST /api/documents returned no document.");
    }

    public async Task UpdateDocumentAsync(string id, string title, string content, CancellationToken ct = default)
    {
        // Response shape wasn't fully verified live — caller re-fetches via GetDocumentAsync instead of parsing this.
        using var resp = await SendAsync(HttpMethod.Patch, $"api/documents/{id}", new { title, content, isPublic = false }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task DeleteDocumentAsync(string id, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"api/documents/{id}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task CreateFromTemplateAsync(string templateDocumentId, CancellationToken ct = default)
    {
        // Response shape wasn't fully verified live — caller reloads the root documents list instead of parsing this.
        using var resp = await SendAsync(HttpMethod.Post, "api/documents/from-template",
            new { templateDocumentId, targetFolderId = (string?)null }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // ── Folder management ───────────────────────────────────────────────────────
    // GetDocumentFoldersAsync (above) already returns each folder with its
    // embedded documents; these mutate the folder tree. Request shape { name,
    // parentId } verified against the OpenAPI spec 2026-07-31.

    public Task CreateDocumentFolderAsync(string name, string? parentId = null, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/documents/folders", new { name, parentId }, ct);

    public Task RenameDocumentFolderAsync(string id, string name, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/documents/folders/{id}", new { name }, ct);

    public Task DeleteDocumentFolderAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/documents/folders/{id}", null, ct);

    public Task CreateDocumentInFolderAsync(string folderId, string title, string content, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/documents/folders/{folderId}/documents",
            new { title, content, isPublic = false }, ct);

    // ── Share links (create a public read link for a document) ──────────────────
    // Same shape as list share-links (verified live 2026-08-01).

    public async Task<List<ShareLink>> GetDocumentShareLinksAsync(string documentId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/documents/{documentId}/share-links", ct);
        return json.TryGetProperty("shareLinks", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ShareLink>>(JsonOptions) ?? new()
            : new();
    }

    public Task<ShareLink> CreateDocumentShareLinkAsync(string documentId, CancellationToken ct = default)
        => SendJsonAsync<ShareLink>(HttpMethod.Post, $"api/documents/{documentId}/share-links", new { }, ct);

    public Task DeleteDocumentShareLinkAsync(string documentId, string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/documents/{documentId}/share-links/{token}", null, ct);
}
