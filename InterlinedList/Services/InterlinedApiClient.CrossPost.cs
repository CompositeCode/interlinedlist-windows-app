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
