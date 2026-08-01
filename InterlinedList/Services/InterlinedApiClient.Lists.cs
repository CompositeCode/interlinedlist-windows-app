using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Lists (Airtable-like freeform-row data). There is a schema endpoint
/// (PUT api/lists/{id}/schema) but it's skipped here — rows work fine as
/// freeform JSON without ever defining one, and the schema DSL's validation
/// rules were never fully confirmed live.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<ListsPage> GetListsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/lists?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ListsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/lists returned no body.");
    }

    public async Task<ListSummary> CreateListAsync(string title, string? description, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/lists", new { title, description }, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("data").Deserialize<ListSummary>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "POST /api/lists returned no data.");
    }

    public async Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"api/lists/{listId}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<ListDataPage> GetListDataAsync(string listId, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/lists/{listId}/data?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ListDataPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/lists/{id}/data returned no body.");
    }

    public async Task AddListRowAsync(string listId, Dictionary<string, object?> rowData, CancellationToken ct = default)
    {
        // Write-path response shape wasn't fully verified live — re-fetch rows afterward instead of parsing this.
        using var resp = await SendAsync(HttpMethod.Post, $"api/lists/{listId}/data", new { data = rowData }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // GET /api/lists/{id} returns the list metadata under a "data" envelope
    // (verified live 2026-07-31).
    public async Task<ListSummary> GetListAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}", ct);
        return json.GetProperty("data").Deserialize<ListSummary>(JsonOptions)
            ?? throw new InterlinedApiException(200, "GET /api/lists/{id} returned no data.");
    }

    public Task UpdateListAsync(string listId, string title, string? description, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/lists/{listId}", new { title, description }, ct);

    // Edit/delete of an individual row — the pieces that made rows write-once
    // before. Same read-after-write discipline as AddListRowAsync.
    public Task UpdateListRowAsync(string listId, string rowId, Dictionary<string, object?> rowData, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/lists/{listId}/data/{rowId}", new { data = rowData }, ct);

    public Task DeleteListRowAsync(string listId, string rowId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/lists/{listId}/data/{rowId}", null, ct);

    /// <summary>
    /// Lists owned by others that have been shared with the current user
    /// (GET /api/lists/watching → { lists, pagination }). Their rows are readable
    /// via <see cref="GetListDataAsync"/> (access is granted server-side).
    /// </summary>
    public async Task<List<WatchedList>> GetWatchingListsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/lists/watching", ct);
        return json.TryGetProperty("lists", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<WatchedList>>(JsonOptions) ?? new()
            : new();
    }

    // ── Share links (create a public read link for a list) ──────────────────────
    // Shape verified live 2026-08-01: GET → { shareLinks }, POST → the new link,
    // DELETE …/{token} revokes it.

    public async Task<List<ShareLink>> GetListShareLinksAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/share-links", ct);
        return json.TryGetProperty("shareLinks", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ShareLink>>(JsonOptions) ?? new()
            : new();
    }

    public Task<ShareLink> CreateListShareLinkAsync(string listId, CancellationToken ct = default)
        => SendJsonAsync<ShareLink>(HttpMethod.Post, $"api/lists/{listId}/share-links", new { }, ct);

    public Task DeleteListShareLinkAsync(string listId, string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/lists/{listId}/share-links/{token}", null, ct);
}
