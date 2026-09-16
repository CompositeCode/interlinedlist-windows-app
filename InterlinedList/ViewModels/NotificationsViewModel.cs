using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // ── Full feed (the "See all" view) ──────────────────────────────────────
    // A separate collection from the tray `Items`, so opening the full view
    // doesn't disturb the rail and the two can hold different scopes at once.

    public ObservableCollection<NotificationItemViewModel> FeedItems { get; } = new();

    /// <summary>
    /// How many feed items to request. The endpoint has NO offset — it is
    /// silently ignored — so "load more" raises this and re-fetches rather than
    /// asking for a next page.
    /// </summary>
    private int _feedLimit = InitialFeedLimit;

    private const int InitialFeedLimit = 50;
    private const int FeedLimitStep = 50;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreFeedCommand))]
    private bool isLoadingFeed;

    /// <summary>
    /// True while the last fetch filled the requested limit — i.e. there may be
    /// more. With no `hasMore` flag and no total, a full page is the only signal
    /// the endpoint offers.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreFeedCommand))]
    private bool feedMayHaveMore;

    [ObservableProperty]
    private bool feedIsEmpty;

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
    // ── Full notification feed ──────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadFeedAsync()
    {
        _feedLimit = InitialFeedLimit;
        await FetchFeedAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreFeed))]
    private async Task LoadMoreFeedAsync()
    {
        _feedLimit += FeedLimitStep;
        await FetchFeedAsync();
    }

    private bool CanLoadMoreFeed() => FeedMayHaveMore && !IsLoadingFeed;

    private async Task FetchFeedAsync()
    {
        IsLoadingFeed = true;
        try
        {
            var page = await _session.Api.GetNotificationFeedAsync(_feedLimit);

            // Re-fetch, not append: with no offset, a larger limit returns a
            // superset that already includes everything on screen.
            FeedItems.Clear();
            foreach (var item in page.Items)
                FeedItems.Add(new NotificationItemViewModel(item));

            UnreadCount = page.UnreadCount;
            FeedMayHaveMore = page.Items.Count >= _feedLimit;
            FeedIsEmpty = FeedItems.Count == 0;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingFeed = false;
        }
    }

    /// <summary>
    /// Mark read, then navigate — the documented web behaviour ("clicking a
    /// notification marks it read and takes you straight to the related
    /// content").
    /// </summary>
    [RelayCommand]
    private async Task ActivateAsync(NotificationItemViewModel item)
    {
        if (item is null) return;

        if (item.IsUnread)
        {
            try
            {
                await _session.Api.MarkNotificationReadAsync(item.Id);
                item.MarkRead();
                UnreadCount = Items.Concat(FeedItems).Count(i => i.IsUnread);
            }
            catch (InterlinedApiException)
            {
                // Failing to mark read must not block navigation.
            }
        }

        item.Navigate();
    }
}
