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
}
