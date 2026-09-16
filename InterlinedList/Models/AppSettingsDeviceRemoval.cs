namespace InterlinedList.Models;

/// <summary>
/// Result of DELETE /api/user/app-settings/{appKey}/devices/{deviceId} —
/// <c>{"deleted":true,"promotedDeviceId":"…"}</c> (verified live 2026-09-16).
/// Deregistering also deletes that device's settings document, and if the
/// removed device was the default the most recently seen remaining device is
/// promoted in its place and named in <see cref="PromotedDeviceId"/>. Removing
/// a non-default device (or the last one) returns a null promotion.
/// </summary>
public sealed class AppSettingsDeviceRemoval
{
    public bool Deleted { get; init; }
    public string? PromotedDeviceId { get; init; }
}
