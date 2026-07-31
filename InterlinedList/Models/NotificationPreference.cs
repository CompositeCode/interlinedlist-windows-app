namespace InterlinedList.Models;

/// <summary>
/// One row of GET /api/user/notification-preferences ("events"): a notifiable
/// event and which delivery channels are enabled for it. PATCH the same
/// endpoint to change a channel toggle.
/// </summary>
public sealed class NotificationPreference
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public NotificationChannels Channels { get; init; } = new();
}

public sealed class NotificationChannels
{
    public bool Push { get; init; }
    public bool InApp { get; init; }
}
