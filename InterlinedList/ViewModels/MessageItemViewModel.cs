using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Wraps a single Message for display/interaction in the feed. DigCount/DugByMe
/// are mutable, observable copies (the underlying Message is immutable) so the
/// Dig command can update them optimistically.
/// </summary>
public partial class MessageItemViewModel : ObservableObject
{
    private readonly InterlinedApiClient _api;

    public string Id { get; }
    public string Content { get; }
    public string TimeFormatted { get; }
    public string AuthorDisplayName { get; }
    public string AuthorHandle { get; }
    public string? AvatarUrl { get; }
    public bool IsMine { get; }

    [ObservableProperty]
    private int digCount;

    [ObservableProperty]
    private bool dugByMe;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public MessageItemViewModel(Message message, InterlinedApiClient api, string? currentUserId)
    {
        _api = api;

        Id = message.Id;
        Content = message.Content;
        TimeFormatted = message.TimeFormatted;
        AuthorDisplayName = message.AuthorDisplayName;
        AuthorHandle = message.AuthorHandle;
        AvatarUrl = message.User?.Avatar;
        IsMine = message.UserId == currentUserId;

        digCount = message.DigCount;
        dugByMe = message.DugByMe;
    }

    [RelayCommand]
    private async Task DigAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        var wasDug = DugByMe;
        try
        {
            if (!wasDug)
            {
                DugByMe = true;
                DigCount++;
                await _api.DigAsync(Id);
            }
            else
            {
                DugByMe = false;
                DigCount--;
                await _api.UndigAsync(Id);
            }
        }
        catch (InterlinedApiException ex)
        {
            if (!wasDug)
            {
                DugByMe = false;
                DigCount--;
            }
            else
            {
                DugByMe = true;
                DigCount++;
            }
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
