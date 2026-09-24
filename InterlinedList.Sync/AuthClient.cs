using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Sync;

/// <summary>
/// Mints a bearer sync-token from email/password and persists it to the shared
/// <c>session.dat</c> — so signing in here also signs in the main app, and vice versa.
/// </summary>
internal sealed class AuthClient(HttpClient http, string baseUrl = "https://interlinedlist.com")
{
    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var url = baseUrl.TrimEnd('/') + "/api/auth/sync-token";
        using var resp = await http.PostAsJsonAsync(
            url, new { email, password, deviceName = Environment.MachineName }, ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Sign-in failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct);
        var token = body?.Token ?? throw new HttpRequestException("Sign-in response contained no token.");
        SessionTokenFile.Write(token);
        SyncLog.Info("Signed in; token stored to shared session.dat.");
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
    }
}
