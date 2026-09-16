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
    /// <summary>
    /// The 15 writable fields on <c>PATCH /api/user/update</c>, exactly as the
    /// server names them. Field names, native JSON types and validation were
    /// probed live 2026-09-16 with a throwaway sync-token (every value changed
    /// was captured first and restored afterwards; a follow-up
    /// <c>GET /api/user</c> diffed byte-identical).
    /// </summary>
    /// <remarks>
    /// Findings worth not re-learning the hard way:
    /// <list type="bullet">
    /// <item>The OpenAPI *request* schema types the numbers and booleans as
    /// <c>"string"</c>. That is wrong/loose — native JSON numbers and booleans
    /// are accepted and round-trip correctly. (The *response* schema,
    /// <c>UserWire</c>, does use native types.)</item>
    /// <item><b>Sending an explicit <c>null</c> for a field is a 500, not a
    /// no-op</b> — <c>{"theme": null}</c> returns
    /// <c>500 internal_error</c> and writes nothing. Every writer here must
    /// therefore <i>omit</i> untouched fields rather than send null, which is
    /// why these methods build a sparse dictionary instead of an anonymous
    /// object (<c>JsonSerializerDefaults.Web</c> does not drop nulls).</item>
    /// <item><c>viewingPreference</c> <i>is</i> enum-validated server-side:
    /// <c>my_messages</c>, <c>all_messages</c>, <c>followers_only</c>,
    /// <c>following_only</c>. <c>theme</c> is <b>not</b> validated — the server
    /// accepted an arbitrary string — so readers must treat anything that isn't
    /// <c>light</c>/<c>dark</c> as "follow the OS".</item>
    /// <item>Ranges are enforced with 400s: <c>maxMessageLength</c> 1–10000,
    /// <c>messagesPerPage</c> 10–30, <c>notificationTrayLimit</c> 10–40.</item>
    /// <item>The 200 response carries a <c>user</c> object, but it is
    /// <b>narrower than <c>GET /api/user</c></b> — <c>accountStatus</c>,
    /// <c>cleared</c>, <c>pendingEmail</c>, <c>isAdministrator</c> and the
    /// layout/notification blobs are all absent. Deserializing it into
    /// <see cref="CurrentUser"/> would silently blank the account-status
    /// banner, so callers re-fetch (read-after-write), per the house rule.</item>
    /// </list>
    /// </remarks>
    public static class UserUpdateFields
    {
        public const string DisplayName = "displayName";
        public const string Bio = "bio";
        public const string Avatar = "avatar";
        public const string IsPrivateAccount = "isPrivateAccount";
        public const string GithubDefaultRepo = "githubDefaultRepo";
        public const string Theme = "theme";
        public const string MaxMessageLength = "maxMessageLength";
        public const string MessagesPerPage = "messagesPerPage";
        public const string ViewingPreference = "viewingPreference";
        public const string ShowPreviews = "showPreviews";
        public const string NotificationTrayLimit = "notificationTrayLimit";
        public const string DefaultPubliclyVisible = "defaultPubliclyVisible";
        public const string ShowAdvancedPostSettings = "showAdvancedPostSettings";
        public const string Latitude = "latitude";
        public const string Longitude = "longitude";
    }

    public Task UpdateProfileAsync(
        string? displayName,
        string? bio,
        bool? isPrivateAccount = null,
        string? theme = null,
        CancellationToken ct = default)
    {
        // Sparse on purpose: this used to send an anonymous object, so a plain
        // "Save profile" always shipped `"theme": null` — which the server
        // answers with a 500 (see UserUpdateFields). Omitting untouched fields
        // is the fix, and it also stops a profile save from stomping
        // preferences the user set elsewhere.
        var body = new Dictionary<string, object?>();
        Set(body, UserUpdateFields.DisplayName, displayName);
        Set(body, UserUpdateFields.Bio, bio);
        Set(body, UserUpdateFields.IsPrivateAccount, isPrivateAccount);
        Set(body, UserUpdateFields.Theme, theme);
        return SendVoidAsync(HttpMethod.Patch, "api/user/update", body, ct);
    }

    /// <summary>
    /// Writes the server-side viewing/composer preferences. Every argument is
    /// optional and only the ones supplied are sent — see
    /// <see cref="UserUpdateFields"/> for why null must never go on the wire.
    /// Callers should re-fetch <c>GET /api/user</c> afterwards rather than trust
    /// the (narrower) response envelope.
    /// </summary>
    public Task UpdatePreferencesAsync(
        string? theme = null,
        int? maxMessageLength = null,
        int? messagesPerPage = null,
        string? viewingPreference = null,
        bool? showPreviews = null,
        int? notificationTrayLimit = null,
        bool? defaultPubliclyVisible = null,
        bool? showAdvancedPostSettings = null,
        double? latitude = null,
        double? longitude = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        Set(body, UserUpdateFields.Theme, theme);
        Set(body, UserUpdateFields.MaxMessageLength, maxMessageLength);
        Set(body, UserUpdateFields.MessagesPerPage, messagesPerPage);
        Set(body, UserUpdateFields.ViewingPreference, viewingPreference);
        Set(body, UserUpdateFields.ShowPreviews, showPreviews);
        Set(body, UserUpdateFields.NotificationTrayLimit, notificationTrayLimit);
        Set(body, UserUpdateFields.DefaultPubliclyVisible, defaultPubliclyVisible);
        Set(body, UserUpdateFields.ShowAdvancedPostSettings, showAdvancedPostSettings);
        Set(body, UserUpdateFields.Latitude, latitude);
        Set(body, UserUpdateFields.Longitude, longitude);

        return body.Count == 0
            ? Task.CompletedTask
            : SendVoidAsync(HttpMethod.Patch, "api/user/update", body, ct);
    }

    private static void Set(Dictionary<string, object?> body, string field, object? value)
    {
        if (value is not null) body[field] = value;
    }

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
