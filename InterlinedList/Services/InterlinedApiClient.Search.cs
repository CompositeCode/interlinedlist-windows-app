using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Unified search across messages, people, lists, and documents.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<MessagesPage> SearchMessagesAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/messages/search?q={Uri.EscapeDataString(query)}&limit={limit}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<MessagesPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/messages/search returned no body.");
    }

    public async Task<UsersSearchPage> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/users/search?q={Uri.EscapeDataString(query)}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<UsersSearchPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/users/search returned no body.");
    }

    public async Task<List<ListSearchResult>> SearchListsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/lists/search?q={Uri.EscapeDataString(query)}&limit={limit}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("lists").Deserialize<List<ListSearchResult>>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/lists/search returned no lists.");
    }

    public async Task<List<DocumentSearchResult>> SearchDocumentsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/documents/search?q={Uri.EscapeDataString(query)}&limit={limit}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("documents").Deserialize<List<DocumentSearchResult>>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/documents/search returned no documents.");
    }
}
