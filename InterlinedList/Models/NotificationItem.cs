namespace InterlinedList.Models;

public sealed class NotificationItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? ActionUrl { get; init; }
    public string? Type { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }

    public bool IsUnread => ReadAt is null;
    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");
}
