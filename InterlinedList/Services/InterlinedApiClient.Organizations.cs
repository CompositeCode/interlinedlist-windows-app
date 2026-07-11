using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Only the top-level organization endpoints (list/get/create) are wired up
/// here — GET api/organizations/{id}/members returned 401 with this app's
/// bearer-token auth during live testing, so member-list/management is
/// deliberately not implemented anywhere in this client.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<OrganizationsPage> GetAllOrganizationsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/organizations?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<OrganizationsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/organizations returned no body.");
    }

    public async Task<OrganizationsPage> GetMyOrganizationsAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/user/organizations", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<OrganizationsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/user/organizations returned no body.");
    }

    public async Task<OrganizationSummary> GetOrganizationAsync(string id, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/organizations/{id}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("organization").Deserialize<OrganizationSummary>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/organizations/{id} returned no organization.");
    }

    public async Task CreateOrganizationAsync(string name, string? description, bool isPublic, CancellationToken ct = default)
    {
        // Create response envelope wasn't live-verified — just confirm success and
        // let the caller reload the "my organizations" list, same defensive pattern
        // used for other not-fully-verified writes in this codebase.
        using var resp = await SendAsync(HttpMethod.Post, "api/organizations", new { name, description, isPublic }, ct);
        await EnsureSuccessAsync(resp, ct);
    }
}
