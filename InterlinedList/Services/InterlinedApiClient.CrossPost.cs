using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Connected-accounts (cross-post) endpoints. The per-provider
/// GET /api/auth/{provider}/status endpoints were live-tested and only
/// report whether that OAuth integration is configured on the server at
/// all — not whether this user has linked it — so they're deliberately
/// not used here. GET /api/user/identities is the live-verified source of
/// truth for a user's actual linked accounts.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<List<LinkedIdentity>> GetLinkedIdentitiesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "api/user/identities", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("identities").Deserialize<List<LinkedIdentity>>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/user/identities returned no identities.");
    }

    // Not live-verified — deliberately untested against the shared test
    // account's real linked identities. Only success/failure is checked;
    // there's no known response body to parse.
    public async Task RemoveIdentityAsync(string provider, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"api/user/identities?provider={Uri.EscapeDataString(provider)}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // ── LinkedIn pages ──────────────────────────────────────────────────────────
    // Syncing pages is the prerequisite for GET /api/linkedin/targets returning
    // anything — an account with LinkedIn linked but never synced has no pages
    // to target.

    /// <summary>
    /// Refresh the LinkedIn pages available to this account.
    /// </summary>
    /// <returns>
    /// <see cref="LinkedInSyncOutcome.Synced"/> on success,
    /// <see cref="LinkedInSyncOutcome.NotLinked"/> when LinkedIn isn't connected.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Verified live 2026-09-16 on an account with <b>no</b> LinkedIn identity:
    /// </para>
    /// <code>
    /// POST /api/linkedin/sync-pages
    ///   -> 400 {"error":"Your LinkedIn account is not linked","code":"not_linked"}
    /// </code>
    /// <para>
    /// That is a clean, distinguishable state rather than a generic failure, so
    /// it is returned as an outcome instead of thrown — the caller's correct
    /// response is to offer the connect handoff, not to show an error.
    /// </para>
    /// <para>
    /// <b>Note the inconsistency:</b> on the same unlinked account
    /// <c>GET /api/linkedin/targets</c> returns <c>200 {"targets":[]}</c> and
    /// <c>GET /api/linkedin/posting-targets</c> returns
    /// <c>200 {"targets":[],"orgScopeMissing":false}</c>. So an empty target
    /// list does <b>not</b> mean "not linked" — link state comes from
    /// <c>GET /api/user/identities</c> (see this file's summary), never from an
    /// empty collection.
    /// </para>
    /// <para>
    /// The success body is unverified (the test account has no LinkedIn), so
    /// nothing is parsed from it — re-read targets afterward, per the repo's
    /// read-after-write rule.
    /// </para>
    /// </remarks>
    public async Task<LinkedInSyncOutcome> SyncLinkedInPagesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, "api/linkedin/sync-pages", new { }, ct);

        if (resp.IsSuccessStatusCode)
            return LinkedInSyncOutcome.Synced;

        // Read the machine-readable code rather than matching on prose.
        var body = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code)
                && code.GetString() == "not_linked")
            {
                return LinkedInSyncOutcome.NotLinked;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the normal error path.
        }

        await EnsureSuccessAsync(resp, ct);
        return LinkedInSyncOutcome.Synced; // unreachable: EnsureSuccessAsync throws
    }

    /// <summary>
    /// LinkedIn destinations this account can post to.
    /// </summary>
    /// <remarks>
    /// Returns <c>200 {"targets":[]}</c> even when LinkedIn is not linked
    /// (verified live) — so an empty result is ambiguous on its own. Pair it
    /// with <see cref="GetLinkedIdentitiesAsync"/> to tell "not linked" from
    /// "linked but no pages synced".
    /// </remarks>
    public async Task<List<LinkedInTarget>> GetLinkedInTargetsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/linkedin/targets", ct);
        return json.TryGetProperty("targets", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<LinkedInTarget>>(JsonOptions) ?? []
            : [];
    }

    // Desktop apps can't complete OAuth inline without an embedded browser
    // control (out of scope here — no WebView2). The server mediates the
    // whole flow through interlinedlist.com, so we just hand the URL to the
    // OS default browser and let the user return and press Refresh.
    public void OpenProviderAuthorize(string provider, string? instance = null)
    {
        var url = $"{ApiConfig.BaseUrl}api/auth/{provider}/authorize";
        if (!string.IsNullOrEmpty(instance))
            url += $"?instance={Uri.EscapeDataString(instance)}";

        // UseShellExecute = true is required — without it, Process.Start on
        // .NET Core/5+ won't hand the URL to the OS's default browser.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
