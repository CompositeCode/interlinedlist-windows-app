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
}
