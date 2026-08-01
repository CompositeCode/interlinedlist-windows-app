using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;
using Microsoft.Win32;

namespace InterlinedList.ViewModels;

public partial class DirectMessagesViewModel : ObservableObject
{
    private readonly SessionService _session;

    // Ids of every message currently shown in the thread — used to dedupe
    // messages appended by the near-real-time poll.
    private readonly HashSet<string> _shownMessageIds = new();

    // Polls GetDmThreadUpdatesAsync while a recipient is selected. Fires on the
    // UI thread, so its Tick handler can mutate the Messages collection directly.
    private readonly DispatcherTimer _pollTimer;

    public ObservableCollection<DmRecipient> Recipients { get; } = new();
    public ObservableCollection<DmMessageViewModel> Messages { get; } = new();

    // Images uploaded for the next DM (URLs returned by the upload endpoint).
    public ObservableCollection<string> AttachedImageUrls { get; } = new();

    [ObservableProperty]
    private DmRecipient? selectedRecipient;

    [ObservableProperty]
    private string composeText = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isSending;

    [ObservableProperty]
    private bool isUploadingImage;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasSelection => SelectedRecipient is not null;

    public DirectMessagesViewModel(SessionService session)
    {
        _session = session;
        AttachedImageUrls.CollectionChanged += (_, _) => SendCommand.NotifyCanExecuteChanged();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) => await PollThreadUpdatesAsync();
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
        // Switching conversations: stop polling the old one until the new
        // thread has loaded, then restart against the new recipient.
        _pollTimer.Stop();
        SelectedRecipient = recipient;
        await LoadThreadAsync(recipient);
        _pollTimer.Start();
    }

    private async Task LoadThreadAsync(DmRecipient recipient)
    {
        IsLoading = true;
        try
        {
            var currentUserId = _session.CurrentUser?.Id;
            var thread = await _session.Api.GetDmThreadAsync(recipient.Username);

            Messages.Clear();
            _shownMessageIds.Clear();
            foreach (var message in thread.Items)
            {
                Messages.Add(new DmMessageViewModel(message, currentUserId));
                _shownMessageIds.Add(message.Id);
            }

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
        HasSelection && !IsSending &&
        (!string.IsNullOrWhiteSpace(ComposeText) || AttachedImageUrls.Count > 0);

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
            await _session.Api.SendDmAsync(
                recipient.Id,
                ComposeText.Trim(),
                imageUrls: AttachedImageUrls.Count > 0 ? AttachedImageUrls.ToList() : null);
            ComposeText = "";
            AttachedImageUrls.Clear();
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

    [RelayCommand]
    private async Task AttachImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        IsUploadingImage = true;
        try
        {
            var contentType = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
            await using var stream = File.OpenRead(dlg.FileName);
            var url = await _session.Api.UploadDmImageAsync(stream, Path.GetFileName(dlg.FileName), contentType);
            AttachedImageUrls.Add(url);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (IOException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    [RelayCommand]
    private void RemoveAttachment(string url) => AttachedImageUrls.Remove(url);

    // Trash/restore act on the current user's own messages, then re-fetch the
    // thread (the write endpoints don't return a parsed body — read-after-write).
    [RelayCommand]
    private async Task TrashMessageAsync(DmMessageViewModel message)
    {
        try
        {
            await _session.Api.TrashDmAsync(message.Id);
            if (SelectedRecipient is { } recipient)
                await LoadThreadAsync(recipient);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreMessageAsync(DmMessageViewModel message)
    {
        try
        {
            await _session.Api.RestoreDmAsync(message.Id);
            if (SelectedRecipient is { } recipient)
                await LoadThreadAsync(recipient);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // Lightweight incremental fetch on the poll timer. Appends only messages
    // whose Id isn't already shown; transient poll failures are swallowed so
    // they don't spam the error banner.
    private async Task PollThreadUpdatesAsync()
    {
        if (SelectedRecipient is not { } recipient)
            return;

        try
        {
            var currentUserId = _session.CurrentUser?.Id;
            var updates = await _session.Api.GetDmThreadUpdatesAsync(recipient.Username);

            // The recipient may have changed while the fetch was in flight.
            if (SelectedRecipient?.Username != recipient.Username)
                return;

            foreach (var message in updates)
            {
                if (_shownMessageIds.Add(message.Id))
                    Messages.Add(new DmMessageViewModel(message, currentUserId));
            }
        }
        catch (InterlinedApiException)
        {
            // Transient poll failure — ignore, next tick retries.
        }
    }

    partial void OnSelectedRecipientChanged(DmRecipient? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        SendCommand.NotifyCanExecuteChanged();
        // No selection → nothing to poll.
        if (value is null)
            _pollTimer.Stop();
    }

    partial void OnComposeTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}
