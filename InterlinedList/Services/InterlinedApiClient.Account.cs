using System.IO;
using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Account & settings: profile edit, notification preferences, and API-session
/// (sync-token) management. GET /api/user/sessions + DELETE
/// /api/user/sessions/{id} let a user list and revoke standing tokens — the
/// genuine "sign out this lost device" capability the app previously assumed
/// didn't exist (verified live 2026-07-31; DELETE is the one destructive call
/// here and is exercised only against a session the user explicitly picks).
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task UpdateProfileAsync(
        string? displayName,
        string? bio,
        bool? isPrivateAccount = null,
        string? theme = null,
        CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Patch, "api/user/update",
            new { displayName, bio, isPrivateAccount, theme }, ct);

    public async Task<List<ApiSession>> GetSessionsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user/sessions", ct);
        return json.TryGetProperty("sessions", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ApiSession>>(JsonOptions) ?? new()
            : new();
    }

    public Task RevokeSessionAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/user/sessions/{id}", null, ct);

    public async Task<List<NotificationPreference>> GetNotificationPreferencesAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user/notification-preferences", ct);
        return json.TryGetProperty("events", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<NotificationPreference>>(JsonOptions) ?? new()
            : new();
    }

    // channels is sent as an object { push, inApp }; the GET returns it that way,
    // though the OpenAPI request schema loosely types it as string — verify a
    // real PATCH round-trips before treating this as fully proven.
    public Task SetNotificationPreferenceAsync(string key, bool push, bool inApp, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Patch, "api/user/notification-preferences",
            new { key, channels = new { push, inApp } }, ct);

    // ── Avatar / email / account lifecycle ──────────────────────────────────────
    // Request shapes verified against the OpenAPI spec 2026-07-31: avatar {url},
    // change-email {newEmail}, delete {username,email} (a self-confirmation guard).

    public Task UpdateAvatarFromUrlAsync(string url, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/user/avatar/from-url", new { url }, ct);

    public async Task<string> UploadAvatarAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var json = await SendMultipartAsync("api/user/avatar/upload", content, fileName, contentType, ct: ct);
        return json.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } u ? u : "";
    }

    public Task RequestEmailChangeAsync(string newEmail, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/user/change-email/request", new { newEmail }, ct);

    // Destructive. The body echoes the caller's own username + email as a
    // confirmation guard the server checks — callers should require the user to
    // type these before invoking.
    public Task DeleteAccountAsync(string username, string email, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/user/delete", new { username, email }, ct);
}
