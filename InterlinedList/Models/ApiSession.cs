namespace InterlinedList.Models;

/// <summary>
/// An active sync-token / API session from GET /api/user/sessions. Revocable via
/// DELETE /api/user/sessions/{id} (verified live 2026-07-31 — the endpoint the
/// app itself relies on to let a user cut off a lost device's standing token).
/// </summary>
public sealed class ApiSession
{
    public required string Id { get; init; }
    public string? DeviceLabel { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool IsCurrent { get; init; }

    public string DeviceLabelOrFallback => string.IsNullOrWhiteSpace(DeviceLabel) ? "Unknown device" : DeviceLabel;
    public string CreatedFormatted => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string LastUsedFormatted => LastUsedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
}
