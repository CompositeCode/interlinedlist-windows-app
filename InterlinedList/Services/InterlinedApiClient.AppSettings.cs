using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Application Settings &amp; Devices — the platform's generic per-user, per-app
/// settings store plus a device registry (ten endpoints under
/// /api/user/app-settings/*, documented at /help/api/app-settings). It exists
/// specifically so native companion apps can sync their own preferences across
/// machines, which makes this app its intended consumer. All ten endpoints were
/// live-probed with the bearer sync-token against a throwaway
/// <c>claude-probe</c> appKey on 2026-09-16 (everything created was deleted
/// afterwards); no subscription is required.
///
/// <para><b>Writes are compare-and-swap.</b> Every PUT carries a body
/// <c>baseVersion</c>: 0 creates the document at version 1, otherwise it must
/// equal the document's current <c>version</c>. A mismatch — including a
/// non-zero baseVersion when no document exists — returns 409 with the full
/// current document under <c>current</c>, so the caller can rebase instead of
/// failing. Verified live:</para>
/// <code>
/// {"error":"version_conflict","code":"version_conflict",
///  "current":{"appKey":"claude-probe","scope":"account","deviceId":null,
///             "version":2,"updatedAt":"2026-09-16T19:53:12.395Z",
///             "schemaVersion":42,"settings":{"theme":"dark","token":"x"}}}
/// </code>
/// <para>…and <c>"current":null</c> when there was no document to report (the
/// non-zero-baseVersion-on-a-missing-document case, also observed live). The
/// <c>Save*SettingsAsync</c> helpers close that loop: they rebase onto
/// <c>current</c> and retry rather than surfacing the conflict.</para>
///
/// <para><b>settings stays raw JSON</b> the whole way through — see
/// <see cref="AppSettingsDocument"/> for why (unknown keys must survive, and
/// key order does <i>not</i>, contrary to the published "byte for byte"
/// wording).</para>
///
/// <para>Client-side guards mirror the server: appKey/deviceId regexes and the
/// 64 KB settings cap, all rejected before the request goes out. The cap is
/// measured exactly as the server measures it — UTF-8 bytes of the serialized
/// <c>settings</c> object only, not the envelope: 65536 bytes was accepted and
/// 65537 rejected with 413, and a 32774-character payload weighing 65538 UTF-8
/// bytes was also rejected, confirming bytes rather than characters.</para>
///
/// <para><b>Rate limit.</b> The docs advertise ~60 writes/min per user per
/// appKey shared across every write method on that key, with
/// <c>RateLimit-*</c> + <c>Retry-After</c> on a 429. It could not be provoked
/// live (95 sequential PUTs at ~70/min and an 80-way parallel burst both came
/// back clean, and no RateLimit-* headers appear on successful writes), so the
/// 429 path here is written from the documented contract: honor
/// <c>Retry-After</c>, retry a bounded number of times, then surface
/// <see cref="AppSettingsRateLimitException"/>. Reads are not rate-limited.</para>
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>Server cap on the serialized settings object, in UTF-8 bytes (413 above this).</summary>
    public const int MaxSettingsBytes = 65536;

    /// <summary>Device display names are trimmed and capped at 120 characters server-side.</summary>
    public const int MaxDeviceNameLength = 120;

    private const int DefaultSaveAttempts = 5;
    private const int MaxRateLimitRetries = 2;
    private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(60);

    private static readonly Regex AppKeyPattern =
        new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DeviceIdPattern =
        new("^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The server measures the settings cap on its own parsed JSON, where
    // non-ASCII characters are literal UTF-8 (confirmed live: 32764 'é' chars =
    // 65528 bytes tripped the cap). The default web encoder escapes them as
    // \uXXXX, which would overstate the size ~3x and reject valid payloads, so
    // measurement uses the relaxed encoder to match the server byte for byte.
    private static readonly JsonSerializerOptions SettingsMeasureOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly JsonElement EmptySettingsObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>An empty settings object, for seeding a first-ever write.</summary>
    public static JsonElement EmptySettings => EmptySettingsObject;

    // ── Reads (not rate-limited) ────────────────────────────────────────────────

    /// <summary>
    /// Account-scoped (shared) document. Returns null on 404 — "nothing synced
    /// yet for this app" is the normal first-run answer, not an error.
    /// </summary>
    public Task<AppSettingsDocument?> GetAccountSettingsAsync(string appKey, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        return GetSettingsDocumentOrNullAsync($"api/user/app-settings/{appKey}", ct);
    }

    /// <summary>
    /// Device-scoped document. Returns null on 404, which covers both "this
    /// device has no document yet" and "the device isn't registered".
    /// </summary>
    public Task<AppSettingsDocument?> GetDeviceSettingsAsync(string appKey, string deviceId, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);
        return GetSettingsDocumentOrNullAsync($"api/user/app-settings/{appKey}/devices/{deviceId}/settings", ct);
    }

    /// <summary>Registered machines for this user and app, newest-seen first.</summary>
    public async Task<List<AppSettingsDevice>> GetAppDevicesAsync(string appKey, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        var json = await GetElementAsync($"api/user/app-settings/{appKey}/devices", ct);
        return json.TryGetProperty("devices", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<AppSettingsDevice>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// What a fresh machine should seed from. A 404 carrying
    /// <c>{"source":"none"}</c> is the documented "nothing to seed from" answer
    /// and comes back as <see cref="AppSettingsBootstrap.SourceNone"/> rather
    /// than an exception.
    /// </summary>
    public async Task<AppSettingsBootstrap> GetAppSettingsBootstrapAsync(
        string appKey, string deviceId, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);

        var path = $"api/user/app-settings/{appKey}/bootstrap?deviceId={Uri.EscapeDataString(deviceId)}";
        using var resp = await SendAsync(HttpMethod.Get, path, body: null, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            // {"source":"none"} — but fall back to "none" for any other 404 body
            // shape so a route change can't be mistaken for seedable settings.
            var missing = await ReadRootElementAsync(resp, ct);
            return new AppSettingsBootstrap
            {
                Source = missing.ValueKind == JsonValueKind.Object
                    && missing.TryGetProperty("source", out var s)
                    && s.GetString() is { Length: > 0 } src
                        ? src
                        : AppSettingsBootstrap.SourceNone,
            };
        }

        await EnsureSuccessAsync(resp, ct);
        var root = await ReadRootElementAsync(resp, ct);
        var source = root.TryGetProperty("source", out var sourceProp)
            ? sourceProp.GetString() ?? AppSettingsBootstrap.SourceNone
            : AppSettingsBootstrap.SourceNone;

        return new AppSettingsBootstrap
        {
            Source = source,
            // The document's fields are merged into the response root, not nested.
            Document = root.TryGetProperty("settings", out _) ? ParseSettingsDocument(root) : null,
            DefaultDeviceId = ReadString(root, "defaultDeviceId"),
            DefaultDeviceName = ReadString(root, "defaultDeviceName"),
        };
    }

    // ── Compare-and-swap writes ─────────────────────────────────────────────────

    /// <summary>
    /// Raw CAS write of the account document. Throws
    /// <see cref="AppSettingsVersionConflictException"/> (carrying the server's
    /// <c>current</c>) when <paramref name="baseVersion"/> is stale — prefer
    /// <see cref="SaveAccountSettingsAsync"/>, which rebases for you.
    /// </summary>
    public Task<AppSettingsDocument> PutAccountSettingsAsync(
        string appKey,
        JsonElement settings,
        int baseVersion,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        return PutSettingsAsync($"api/user/app-settings/{appKey}", settings, baseVersion, schemaVersion, ct);
    }

    /// <summary>
    /// Raw CAS write of a device document. The device must already be
    /// registered — writing to an unregistered deviceId returns
    /// <c>404 {"error":"device not registered"}</c> (verified live), surfaced as
    /// <see cref="InterlinedApiException"/> with status 404.
    /// </summary>
    public Task<AppSettingsDocument> PutDeviceSettingsAsync(
        string appKey,
        string deviceId,
        JsonElement settings,
        int baseVersion,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);
        return PutSettingsAsync(
            $"api/user/app-settings/{appKey}/devices/{deviceId}/settings", settings, baseVersion, schemaVersion, ct);
    }

    /// <summary>
    /// Read-modify-write of the account document with automatic rebase:
    /// <paramref name="rebase"/> is handed the current document (null when none
    /// exists) and returns the settings to store. On a 409 it is called again
    /// with the conflict's <c>current</c> — so a concurrent writer costs a retry
    /// instead of an error. The delegate must merge, not overwrite blindly, or
    /// the rebase is pointless.
    /// </summary>
    public async Task<AppSettingsDocument> SaveAccountSettingsAsync(
        string appKey,
        Func<AppSettingsDocument?, JsonElement> rebase,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        int maxAttempts = DefaultSaveAttempts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rebase);
        ValidateAppKey(appKey);

        var current = await GetAccountSettingsAsync(appKey, ct);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PutAccountSettingsAsync(
                    appKey, rebase(current), current?.Version ?? 0, schemaVersion, ct);
            }
            catch (AppSettingsVersionConflictException ex) when (attempt < Math.Max(1, maxAttempts))
            {
                // The 409 body carries the full current document; re-read only in
                // the rare "current": null case (a concurrent create won the race).
                current = ex.Current ?? await GetAccountSettingsAsync(appKey, ct);
            }
        }
    }

    /// <summary>Read-modify-write of a device document with automatic rebase. See <see cref="SaveAccountSettingsAsync"/>.</summary>
    public async Task<AppSettingsDocument> SaveDeviceSettingsAsync(
        string appKey,
        string deviceId,
        Func<AppSettingsDocument?, JsonElement> rebase,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        int maxAttempts = DefaultSaveAttempts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rebase);
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);

        var current = await GetDeviceSettingsAsync(appKey, deviceId, ct);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PutDeviceSettingsAsync(
                    appKey, deviceId, rebase(current), current?.Version ?? 0, schemaVersion, ct);
            }
            catch (AppSettingsVersionConflictException ex) when (attempt < Math.Max(1, maxAttempts))
            {
                current = ex.Current ?? await GetDeviceSettingsAsync(appKey, deviceId, ct);
            }
        }
    }

    /// <summary>
    /// Last-writer-wins convenience over <see cref="SaveAccountSettingsAsync"/>:
    /// replaces the stored settings wholesale, rebasing only the version. Use it
    /// when this app owns the whole document; use the rebase overload when other
    /// keys must survive.
    /// </summary>
    public Task<AppSettingsDocument> OverwriteAccountSettingsAsync(
        string appKey,
        JsonElement settings,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        CancellationToken ct = default)
        => SaveAccountSettingsAsync(appKey, _ => settings, schemaVersion, DefaultSaveAttempts, ct);

    /// <summary>Last-writer-wins convenience over <see cref="SaveDeviceSettingsAsync"/>.</summary>
    public Task<AppSettingsDocument> OverwriteDeviceSettingsAsync(
        string appKey,
        string deviceId,
        JsonElement settings,
        int schemaVersion = AppSettingsDocument.DefaultSchemaVersion,
        CancellationToken ct = default)
        => SaveDeviceSettingsAsync(appKey, deviceId, _ => settings, schemaVersion, DefaultSaveAttempts, ct);

    /// <summary>
    /// Deletes the account document. Idempotent: returns true when a document
    /// was removed, false when there was nothing to delete (both are 200 —
    /// verified live).
    /// </summary>
    public async Task<bool> DeleteAccountSettingsAsync(string appKey, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        var root = await SendAppSettingsWriteAsync(
            HttpMethod.Delete, $"api/user/app-settings/{appKey}", body: null, conflictAware: false, ct);
        return root.TryGetProperty("deleted", out var deleted)
            && deleted.ValueKind == JsonValueKind.True;
    }

    // ── Device registry ─────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new device or refreshes an existing one (keyed on deviceId —
    /// a refresh updates name/platform/versions/last-seen and keeps the default
    /// flag). The first device registered for an app becomes the default.
    /// <paramref name="appDisplayName"/> only seeds the shared app-catalog entry
    /// the first time an appKey is seen and is ignored afterwards.
    /// </summary>
    public async Task<AppSettingsDevice> RegisterAppDeviceAsync(
        string appKey,
        string deviceId,
        string deviceName,
        string platform = AppSettingsDevice.PlatformWindows,
        string? appVersion = null,
        string? osVersion = null,
        string? appDisplayName = null,
        CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);
        ValidateDeviceName(deviceName);
        if (!AppSettingsDevice.Platforms.Contains(platform))
            throw new InterlinedApiException(400,
                $"platform must be one of {string.Join(", ", AppSettingsDevice.Platforms)}.");

        var root = await SendAppSettingsWriteAsync(
            HttpMethod.Post, $"api/user/app-settings/{appKey}/devices",
            new { deviceId, deviceName = deviceName.Trim(), platform, appVersion, osVersion, appDisplayName },
            conflictAware: false, ct);
        return ParseDevice(root, "POST /api/user/app-settings/{appKey}/devices");
    }

    /// <summary>
    /// Renames a device and/or promotes it to the default ("main workstation"),
    /// which demotes the previous default atomically. At least one of
    /// <paramref name="deviceName"/> / <paramref name="isDefault"/> is required
    /// — an empty body is a 400 (verified live), so that is caught here.
    /// Passing <c>isDefault: false</c> is not a demotion: promote another device
    /// instead.
    /// </summary>
    public async Task<AppSettingsDevice> UpdateAppDeviceAsync(
        string appKey,
        string deviceId,
        string? deviceName = null,
        bool? isDefault = null,
        CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);
        if (deviceName is null && isDefault is null)
            throw new InterlinedApiException(400, "At least one of deviceName or isDefault is required.");
        if (deviceName is not null)
            ValidateDeviceName(deviceName);

        var root = await SendAppSettingsWriteAsync(
            HttpMethod.Patch, $"api/user/app-settings/{appKey}/devices/{deviceId}",
            new { deviceName = deviceName?.Trim(), isDefault },
            conflictAware: false, ct);
        return ParseDevice(root, "PATCH /api/user/app-settings/{appKey}/devices/{deviceId}");
    }

    /// <summary>
    /// Deregisters a device and deletes its device-scoped document. Unlike the
    /// account-document delete this is <i>not</i> idempotent: a second call is a
    /// 404 (verified live).
    /// </summary>
    public async Task<AppSettingsDeviceRemoval> DeregisterAppDeviceAsync(
        string appKey, string deviceId, CancellationToken ct = default)
    {
        ValidateAppKey(appKey);
        ValidateDeviceId(deviceId);

        var root = await SendAppSettingsWriteAsync(
            HttpMethod.Delete, $"api/user/app-settings/{appKey}/devices/{deviceId}",
            body: null, conflictAware: false, ct);
        return new AppSettingsDeviceRemoval
        {
            Deleted = root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True,
            PromotedDeviceId = ReadString(root, "promotedDeviceId"),
        };
    }

    // ── Settings payload helpers (public: the ViewModels need them too) ─────────

    /// <summary>
    /// Parses settings JSON text into the raw element the write path sends.
    /// Rejects anything that isn't a JSON object, which the server also rejects
    /// (<c>400 "settings must be a JSON object"</c>).
    /// </summary>
    public static JsonElement ParseSettingsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptySettingsObject;

        JsonElement element;
        try
        {
            using var doc = JsonDocument.Parse(json);
            element = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InterlinedApiException(400, $"Settings JSON could not be parsed: {ex.Message}");
        }

        if (element.ValueKind != JsonValueKind.Object)
            throw new InterlinedApiException(400, "Settings must be a JSON object.");
        return element;
    }

    /// <summary>
    /// UTF-8 byte length of the serialized settings object — the exact quantity
    /// the server caps at <see cref="MaxSettingsBytes"/>.
    /// </summary>
    public static int MeasureSettingsBytes(JsonElement settings)
        => JsonSerializer.SerializeToUtf8Bytes(settings, SettingsMeasureOptions).Length;

    // ── Private plumbing ───────────────────────────────────────────────────────

    private async Task<AppSettingsDocument> PutSettingsAsync(
        string path, JsonElement settings, int baseVersion, int schemaVersion, CancellationToken ct)
    {
        if (baseVersion < 0)
            throw new InterlinedApiException(400, "baseVersion must be an integer >= 0.");
        if (schemaVersion < 0)
            throw new InterlinedApiException(400, "schemaVersion must be an integer >= 0.");
        if (settings.ValueKind != JsonValueKind.Object)
            throw new InterlinedApiException(400, "Settings must be a JSON object.");

        var bytes = MeasureSettingsBytes(settings);
        if (bytes > MaxSettingsBytes)
            throw new InterlinedApiException(413,
                $"Settings are {bytes:N0} bytes; the limit is {MaxSettingsBytes:N0} bytes (UTF-8, settings object only).");

        // settings goes on the wire as the raw JsonElement — never a re-serialized
        // POCO — so unknown keys from other clients survive untouched.
        var root = await SendAppSettingsWriteAsync(
            HttpMethod.Put, path, new { baseVersion, schemaVersion, settings }, conflictAware: true, ct);
        return ParseSettingsDocument(root);
    }

    /// <summary>
    /// Every write on an appKey shares one rate-limit budget, so all four write
    /// verbs funnel through here: 429 waits out <c>Retry-After</c> a bounded
    /// number of times, and (when <paramref name="conflictAware"/>) 409 is
    /// turned into a typed conflict carrying the server's <c>current</c>.
    /// </summary>
    private async Task<JsonElement> SendAppSettingsWriteAsync(
        HttpMethod method, string path, object? body, bool conflictAware, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var resp = await SendAsync(method, path, body, ct);

            if (conflictAware && resp.StatusCode == HttpStatusCode.Conflict)
                throw BuildVersionConflict(await ReadRootElementAsync(resp, ct), body);

            if ((int)resp.StatusCode == 429)
            {
                var retryAfter = ReadRetryAfter(resp);
                var wait = retryAfter ?? DefaultRateLimitWait;
                if (attempt >= MaxRateLimitRetries || wait > MaxRateLimitWait)
                    throw BuildRateLimit(resp, retryAfter);

                await Task.Delay(wait, ct);
                continue;
            }

            await EnsureSuccessAsync(resp, ct);
            return await ReadRootElementAsync(resp, ct);
        }
    }

    private async Task<AppSettingsDocument?> GetSettingsDocumentOrNullAsync(string path, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, body: null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(resp, ct);
        return ParseSettingsDocument(await ReadRootElementAsync(resp, ct));
    }

    private static async Task<JsonElement> ReadRootElementAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return default;
        try
        {
            using var doc = JsonDocument.Parse(text);
            // Clone so the element outlives the JsonDocument that owns its buffer.
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Mapped by hand rather than via Deserialize&lt;T&gt; so <c>settings</c> is
    /// explicitly cloned as a raw element and a missing/odd payload degrades to
    /// an empty object instead of throwing.
    /// </summary>
    private static AppSettingsDocument ParseSettingsDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InterlinedApiException(500, "App-settings response was not a JSON object.");

        return new AppSettingsDocument
        {
            AppKey = ReadString(root, "appKey") ?? "",
            Scope = ReadString(root, "scope") ?? AppSettingsDocument.ScopeAccount,
            DeviceId = ReadString(root, "deviceId"),
            Version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var version) ? version : 0,
            UpdatedAt = root.TryGetProperty("updatedAt", out var u)
                && u.ValueKind == JsonValueKind.String
                && u.TryGetDateTimeOffset(out var updatedAt)
                    ? updatedAt
                    : DateTimeOffset.MinValue,
            SchemaVersion = root.TryGetProperty("schemaVersion", out var s) && s.TryGetInt32(out var schema)
                ? schema
                : AppSettingsDocument.DefaultSchemaVersion,
            Settings = root.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
                ? settings.Clone()
                : EmptySettingsObject,
        };
    }

    private static AppSettingsDevice ParseDevice(JsonElement root, string what)
    {
        // Both write responses wrap the device: {"device":{…}}.
        var element = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("device", out var wrapped)
            ? wrapped
            : root;
        return element.Deserialize<AppSettingsDevice>(JsonOptions)
            ?? throw new InterlinedApiException(500, $"{what} returned no device.");
    }

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;

    private static AppSettingsVersionConflictException BuildVersionConflict(JsonElement root, object? body)
    {
        // {"error":"version_conflict","code":"version_conflict","current":{…}|null}
        var current = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("current", out var cur)
            && cur.ValueKind == JsonValueKind.Object
                ? ParseSettingsDocument(cur)
                : null;

        var sent = body is not null && TryReadBaseVersion(body, out var baseVersion) ? baseVersion : -1;
        var message = current is not null
            ? $"Settings were changed by another client (sent baseVersion {sent}, current version {current.Version})."
            : $"Settings write conflicted (sent baseVersion {sent}); the server reported no current document.";
        return new AppSettingsVersionConflictException(sent, current, message);
    }

    private static bool TryReadBaseVersion(object body, out int baseVersion)
    {
        baseVersion = -1;
        var prop = body.GetType().GetProperty("baseVersion");
        if (prop?.GetValue(body) is int value)
        {
            baseVersion = value;
            return true;
        }
        return false;
    }

    private static AppSettingsRateLimitException BuildRateLimit(HttpResponseMessage resp, TimeSpan? retryAfter)
        => new(
            retryAfter,
            ReadIntHeader(resp, "RateLimit-Limit"),
            ReadIntHeader(resp, "RateLimit-Remaining"),
            ReadHeader(resp, "RateLimit-Reset"),
            retryAfter is { } wait
                ? $"App-settings write rate limit reached; retry in {wait.TotalSeconds:N0}s."
                : "App-settings write rate limit reached (~60 writes/min per app); retry shortly.");

    // ReadRetryAfter / ReadHeader / ReadIntHeader intentionally live in
    // InterlinedApiClient.cs, not here. This file and that one are the SAME
    // partial class, so defining them in both is CS0111 — which is exactly what
    // happened when #130 and #140 were developed in parallel and merged: git
    // saw no conflict (different files), the compiler did. If you need another
    // header reader, add it there.

    private static void ValidateAppKey(string appKey)
    {
        if (string.IsNullOrEmpty(appKey) || !AppKeyPattern.IsMatch(appKey))
            throw new InterlinedApiException(400,
                "appKey must be 1-64 characters of lowercase letters, digits or hyphens, starting with a letter or digit.");
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || !DeviceIdPattern.IsMatch(deviceId))
            throw new InterlinedApiException(400,
                "deviceId must be 8-128 characters of letters, digits or . _ : - and start with a letter or digit.");
    }

    private static void ValidateDeviceName(string deviceName)
    {
        var trimmed = deviceName?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxDeviceNameLength)
            throw new InterlinedApiException(400,
                $"Device name must be 1-{MaxDeviceNameLength} characters.");
    }
}

/// <summary>
/// A 409 <c>version_conflict</c> from an app-settings write, carrying the
/// server's <c>current</c> document so the caller can rebase and retry.
/// <see cref="InterlinedApiClient.SaveAccountSettingsAsync"/> and friends do
/// that automatically; this only escapes when the raw <c>Put*</c> methods are
/// used directly or the retry budget runs out.
///
/// <para>Separate from <see cref="InterlinedApiException"/> (which is sealed and
/// carries only status + message) precisely because the rebase needs the body.
/// <see cref="Current"/> is null in the documented edge case where a concurrent
/// create won the race — re-read before retrying when that happens.</para>
/// </summary>
public sealed class AppSettingsVersionConflictException : Exception
{
    public AppSettingsVersionConflictException(int baseVersion, AppSettingsDocument? current, string message)
        : base(message)
    {
        BaseVersion = baseVersion;
        Current = current;
    }

    public int StatusCode => 409;
    public string Code => "version_conflict";

    /// <summary>The baseVersion this client sent, or -1 if it could not be determined.</summary>
    public int BaseVersion { get; }

    /// <summary>The server's current document; null when it reported none.</summary>
    public AppSettingsDocument? Current { get; }
}

/// <summary>
/// A 429 <c>rate_limited</c> from an app-settings write, after the client
/// already waited out <c>Retry-After</c> its allotted number of times. The
/// budget (~60 writes/min) is per user per appKey and shared by the account
/// document, every device document and the device registry, so backing off has
/// to pause all of them, not just the call that failed.
///
/// <para><see cref="Reset"/> is the raw <c>RateLimit-Reset</c> header: the 429
/// could not be provoked live, so whether it counts seconds or a unix timestamp
/// is unverified and it is deliberately left untyped.</para>
/// </summary>
public sealed class AppSettingsRateLimitException : Exception
{
    public AppSettingsRateLimitException(
        TimeSpan? retryAfter, int? limit, int? remaining, string? reset, string message)
        : base(message)
    {
        RetryAfter = retryAfter;
        Limit = limit;
        Remaining = remaining;
        Reset = reset;
    }

    public int StatusCode => 429;
    public string Code => "rate_limited";

    public TimeSpan? RetryAfter { get; }
    public int? Limit { get; }
    public int? Remaining { get; }
    public string? Reset { get; }
}
