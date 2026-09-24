namespace InterlinedList.Models;

/// <summary>
/// GET /api/user/app-settings/{appKey}/bootstrap?deviceId= — what a brand-new
/// machine should seed itself from, with provenance. The server merges the
/// resolved document's fields into the response root alongside <c>source</c>
/// (they are NOT nested under a "doc" key), so this type splits them back
/// apart: <see cref="Source"/> plus the resolved <see cref="Document"/>.
///
/// <para>Resolution is first-match in priority order: <c>self</c> (this device
/// already has its own document) → <c>default-device</c> (the default machine's
/// document) → <c>account</c> (the shared document) → <c>none</c>.
/// <b>"none" arrives as HTTP 404 with the body <c>{"source":"none"}</c></b>
/// (verified live 2026-09-16) — that is the normal first-run answer, not an
/// error, so the client surfaces it as <see cref="Source"/> = "none" with a
/// null <see cref="Document"/>.</para>
///
/// <para>A live probe also showed <c>default-device</c> only wins when the
/// default machine actually has a device document: with a default device that
/// had none, a fresh deviceId resolved straight to <c>account</c>. For
/// <c>default-device</c> the response additionally carries
/// <see cref="DefaultDeviceId"/>/<see cref="DefaultDeviceName"/>, and the
/// document's own <c>scope</c>/<c>deviceId</c> describe the <i>source</i>
/// machine — not the requesting device.</para>
/// </summary>
public sealed class AppSettingsBootstrap
{
    public const string SourceSelf = "self";
    public const string SourceDefaultDevice = "default-device";
    public const string SourceAccount = "account";
    public const string SourceNone = "none";

    public required string Source { get; init; }

    /// <summary>Null when <see cref="Source"/> is <see cref="SourceNone"/>.</summary>
    public AppSettingsDocument? Document { get; init; }

    /// <summary>Only sent for <see cref="SourceDefaultDevice"/>.</summary>
    public string? DefaultDeviceId { get; init; }

    /// <summary>Only sent for <see cref="SourceDefaultDevice"/>.</summary>
    public string? DefaultDeviceName { get; init; }

    public bool HasSettings => Document is not null;

    public string SourceLabel => Source switch
    {
        SourceSelf => "This device's own settings",
        SourceDefaultDevice => $"Main workstation ({DefaultDeviceName ?? DefaultDeviceId ?? "unknown"})",
        SourceAccount => "Shared account settings",
        _ => "Nothing to seed from",
    };
}
