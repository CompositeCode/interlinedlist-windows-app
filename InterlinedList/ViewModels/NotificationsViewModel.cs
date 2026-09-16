using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class NotificationsViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private int unreadCount;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>
    /// Dig/push totals received on the user's own messages. Null until loaded,
    /// or when the endpoint is unavailable — the rail hides the strip rather
    /// than showing zeros that might not be zeros.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEngagement))]
    [NotifyPropertyChangedFor(nameof(EngagementSummary))]
    private UserEngagement? engagement;

    public bool HasEngagement => Engagement is not null;

    public string EngagementSummary => Engagement is { } e
        ? $"{e.TotalDigs} Digs · {e.TotalPushes} Pushes on your posts"
        : string.Empty;

    public NotificationsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var page = await _session.Api.GetNotificationsAsync(limit: 20);

            Items.Clear();
            foreach (var item in page.Items)
                Items.Add(new NotificationItemViewModel(item));

            UnreadCount = page.UnreadCount;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }

        // Separate try: engagement is supplementary, so a failure here must not
        // blank the notification list or surface as the rail's error. It is also
        // the endpoint CLAUDE.md wrongly recorded as 401-only, so treat an
        // unexpected failure as "just don't show it".
        try
        {
            Engagement = await _session.Api.GetEngagementAsync();
        }
        catch (InterlinedApiException)
        {
            Engagement = null;
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await _session.Api.MarkAllNotificationsReadAsync();

            foreach (var item in Items)
                item.MarkRead();

            UnreadCount = 0;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MarkOneReadAsync(NotificationItemViewModel item)
    {
        try
        {
            await _session.Api.MarkNotificationReadAsync(item.Id);
            item.MarkRead();
            UnreadCount = Items.Count(i => i.IsUnread);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteOneAsync(NotificationItemViewModel item)
    {
        try
        {
            await _session.Api.DeleteNotificationAsync(item.Id);
            Items.Remove(item);
            UnreadCount = Items.Count(i => i.IsUnread);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
