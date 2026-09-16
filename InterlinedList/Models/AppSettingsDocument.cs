using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// A per-user, per-app settings document from /api/user/app-settings/{appKey}
/// (account scope) or .../devices/{deviceId}/settings (device scope).
///
/// <para><b>Settings is deliberately raw JSON.</b> The server treats the payload
/// as opaque: it never interprets or logs it, and it never drops or renames a
/// key — even keys named like secrets (probed live 2026-09-16 with keys named
/// <c>token</c> and <c>apiKey</c>; both came back intact). Deserializing it into
/// a typed POCO and re-serializing would silently discard any key this app
/// doesn't know about, corrupting another client's settings, so it is held as a
/// <see cref="JsonElement"/> end to end.</para>
///
/// <para><b>Key order is NOT preserved</b>, despite the "byte for byte" wording
/// in the published docs. Live probe: sending
/// <c>{"zeta":…,"token":…,"alpha":…}</c> came back as
/// <c>{"big":…,"zeta":…,"alpha":…}</c> — keys reordered shortest-first then
/// lexicographically at every nesting level (Postgres <c>jsonb</c> ordering).
/// Every key, value, unicode string, null, empty container and number survived
/// exactly; only the ordering moved. Never diff raw settings text to detect
/// changes — compare parsed values.</para>
/// </summary>
public sealed class AppSettingsDocument
{
    public const string ScopeAccount = "account";
    public const string ScopeDevice = "device";

    /// <summary>Server default when a write omits schemaVersion (verified live).</summary>
    public const int DefaultSchemaVersion = 1;

    public required string AppKey { get; init; }

    /// <summary><see cref="ScopeAccount"/> or <see cref="ScopeDevice"/>.</summary>
    public required string Scope { get; init; }

    /// <summary>Null for the account-scoped (shared) document.</summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Monotonic version: 1 after the first successful write, +1 per write. This
    /// is the value to send back as <c>baseVersion</c> on the next write.
    /// </summary>
    public int Version { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Client-owned and echoed back verbatim; defaults to 1 on write.</summary>
    public int SchemaVersion { get; init; } = DefaultSchemaVersion;

    /// <summary>The opaque client-owned JSON object, held raw (see class remarks).</summary>
    public JsonElement Settings { get; init; }

    public bool IsDeviceScoped => string.Equals(Scope, ScopeDevice, StringComparison.OrdinalIgnoreCase);

    /// <summary>Compact JSON text of <see cref="Settings"/> ("{}" when absent).</summary>
    public string SettingsJson =>
        Settings.ValueKind == JsonValueKind.Object || Settings.ValueKind == JsonValueKind.Array
            ? Settings.GetRawText()
            : "{}";

    public string UpdatedFormatted => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ScopeLabel => IsDeviceScoped ? $"This device ({DeviceId})" : "All devices";
}
