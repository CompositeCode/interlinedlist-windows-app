using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class DirectMessagesViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<DmRecipient> Recipients { get; } = new();
    public ObservableCollection<DmMessageViewModel> Messages { get; } = new();

    [ObservableProperty]
    private DmRecipient? selectedRecipient;

    [ObservableProperty]
    private string composeText = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isSending;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasSelection => SelectedRecipient is not null;

    public DirectMessagesViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var recipients = await _session.Api.GetDmRecipientsAsync();

            Recipients.Clear();
            foreach (var recipient in recipients)
                Recipients.Add(recipient);

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
    private async Task SelectRecipientAsync(DmRecipient recipient)
    {
        SelectedRecipient = recipient;
        await LoadThreadAsync(recipient);
    }

    private async Task LoadThreadAsync(DmRecipient recipient)
    {
        IsLoading = true;
        try
        {
            var currentUserId = _session.CurrentUser?.Id;
            var thread = await _session.Api.GetDmThreadAsync(recipient.Username);

            Messages.Clear();
            foreach (var message in thread.Items)
                Messages.Add(new DmMessageViewModel(message, currentUserId));

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

    private bool CanSend() =>
        HasSelection && !IsSending && !string.IsNullOrWhiteSpace(ComposeText);

    // SendDmAsync doesn't return a parsed message body (see
    // InterlinedApiClient.DirectMessages.cs), so re-fetch the thread after sending.
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedRecipient is not { } recipient)
            return;

        IsSending = true;
        try
        {
            await _session.Api.SendDmAsync(recipient.Id, ComposeText.Trim());
            ComposeText = "";
            await LoadThreadAsync(recipient);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    partial void OnSelectedRecipientChanged(DmRecipient? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnComposeTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}
