namespace InterlinedList.Models;

/// <summary>
/// A machine registered against an appKey — GET/POST
/// /api/user/app-settings/{appKey}/devices and PATCH .../devices/{deviceId}.
/// The first device registered for an app becomes the default ("main
/// workstation") automatically; the default's document is what a fresh machine
/// seeds from via bootstrap.
///
/// <para><see cref="HasDeviceSettings"/> is only populated by the <b>list</b>
/// endpoint — the POST/PATCH write responses omit it (verified live
/// 2026-09-16), so it reads false on a freshly-parsed write response rather
/// than meaning "no device document".</para>
/// </summary>
public sealed class AppSettingsDevice
{
    public const string PlatformWindows = "windows";

    /// <summary>The platform values the server accepts; anything else is a 400.</summary>
    public static readonly string[] Platforms =
        ["macos", "ios", "android", "windows", "linux", "web", "other"];

    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string Platform { get; init; }
    public bool IsDefault { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public string? AppVersion { get; init; }
    public string? OsVersion { get; init; }

    /// <summary>List endpoint only — see class remarks.</summary>
    public bool HasDeviceSettings { get; init; }

    public string LastSeenFormatted => LastSeenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string DefaultLabel => IsDefault ? "Main workstation" : "";
}
