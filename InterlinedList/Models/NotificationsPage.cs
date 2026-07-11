namespace InterlinedList.Models;

public sealed class NotificationsPage
{
    public int UnreadCount { get; init; }
    public required List<NotificationItem> Items { get; init; }
}
