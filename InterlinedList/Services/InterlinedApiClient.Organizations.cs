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

    /// <summary>
    /// Search for users to add as members of an organization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The purpose-built, org-scoped counterpart to <c>GET /api/users/search</c>.
    /// Prefer it for the add-member picker: it is the endpoint the product
    /// intends for this, so it can scope candidates to the organization.
    /// </para>
    /// <para>
    /// <b>Owner-only.</b> Verified live 2026-09-16 — calling it as a
    /// non-owner member returns
    /// <c>403 {"error":"Only organization owners can search for users to add",
    /// "code":"forbidden"}</c>, with or without a <c>q</c>. Note that is
    /// stricter than the rest of member management, which owners <i>and</i>
    /// admins can do, so gate the UI on owner specifically.
    /// </para>
    /// <para>
    /// The success envelope is <b>not</b> live-verified: the test account is
    /// only a <c>member</c> of all three of its organizations, and verifying it
    /// would mean creating an organization on shared test infrastructure, which
    /// this repo deliberately avoids. It is therefore parsed leniently — the
    /// same <c>{users:[…]}</c> / bare-array shapes the user-search endpoint
    /// uses — rather than typed strictly against a guess.
    /// </para>
    /// </remarks>
    public async Task<List<UserSearchResult>> SearchOrgCandidateUsersAsync(
        string orgId, string query, CancellationToken ct = default)
    {
        var path = $"api/organizations/{orgId}/users?q={Uri.EscapeDataString(query)}";
        var json = await GetElementAsync(path, ct);

        // Lenient on purpose — see the remarks. Accept an envelope under either
        // plausible key, or a bare array.
        if (json.ValueKind == JsonValueKind.Array)
            return json.Deserialize<List<UserSearchResult>>(JsonOptions) ?? [];

        foreach (var key in (string[])["users", "results"])
        {
            if (json.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.Deserialize<List<UserSearchResult>>(JsonOptions) ?? [];
        }

        return [];
    }

    public Task RemoveOrgMemberAsync(string orgId, string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/organizations/{orgId}/members/{userId}", null, ct);
}
