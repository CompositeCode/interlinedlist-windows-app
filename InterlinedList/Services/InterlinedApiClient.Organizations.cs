using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Organizations: browse (list/get/create) plus full member management. NOTE:
/// GET api/organizations/{id}/members was previously documented as 401-walled
/// for bearer auth, but re-probing live 2026-07-31 it (and add/update/remove)
/// return 200 with the sync-token — so member management IS implemented now.
/// Member mutations follow the read-after-write pattern (re-fetch members after).
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

    public Task UpdateOrganizationAsync(string orgId, string name, string? description, bool isPublic, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/organizations/{orgId}", new { name, description, isPublic }, ct);

    public Task DeleteOrganizationAsync(string orgId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/organizations/{orgId}", null, ct);

    // ── Member management (bearer-authorized as of 2026-07-31) ──────────────────

    public async Task<List<OrgMember>> GetOrgMembersAsync(string orgId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/organizations/{orgId}/members", ct);
        return json.TryGetProperty("members", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<OrgMember>>(JsonOptions) ?? new()
            : new();
    }

    // Add an existing user (found via the global user search) to the org.
    public Task AddOrgMemberAsync(string orgId, string userId, string role, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/organizations/{orgId}/members", new { userId, role }, ct);

    public Task UpdateOrgMemberRoleAsync(string orgId, string userId, string role, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/organizations/{orgId}/members/{userId}", new { role }, ct);

    public Task RemoveOrgMemberAsync(string orgId, string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/organizations/{orgId}/members/{userId}", null, ct);
}
