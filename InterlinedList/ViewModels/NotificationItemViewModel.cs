using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

public partial class NotificationItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Body { get; }
    public string TimeFormatted { get; }
    public string? ActionUrl { get; }

    [ObservableProperty]
    private bool isUnread;

    public NotificationItemViewModel(NotificationItem item)
    {
        Id = item.Id;
        Title = item.Title;
        Body = item.Body;
        TimeFormatted = item.TimeFormatted;
        ActionUrl = item.ActionUrl;

        isUnread = item.IsUnread;
    }

    public void MarkRead() => IsUnread = false;
}
