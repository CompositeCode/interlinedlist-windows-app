using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;
using Microsoft.Win32;

namespace InterlinedList.ViewModels;

/// <summary>
/// Direct Messages, structured the way the product is
/// (/help/direct-messages): a conversation list as the default view, plus three
/// personal folders — Inbox / Sent / Deleted — as tabs.
///
/// Endpoint map:
///   GET /api/dm/conversations      → <see cref="Conversations"/> (left rail, newest activity first)
///   GET /api/dm?folder=…           → <see cref="FolderItems"/> (the selected tab)
///   GET /api/dm/thread/{username}  → <see cref="Messages"/> (an open conversation; marks it read)
///   GET /api/dm/{id}               → single-row read-after-write refresh
///   GET /api/dm/recipients         → <see cref="Recipients"/>, the "New message" picker only
///
/// Both list endpoints are cursor-paginated and their <c>nextCursor</c> is
/// carried verbatim in <see cref="ConversationsCursor"/> /
/// <see cref="FolderCursor"/> — never parsed or constructed.
/// </summary>
public partial class DirectMessagesViewModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly SessionService _session;

    // Ids of every message currently shown in the thread — used to dedupe
    // messages appended by the near-real-time poll.
    private readonly HashSet<string> _shownMessageIds = new();

    // Polls GetDmThreadUpdatesAsync while a conversation is open. Fires on the
    // UI thread, so its Tick handler can mutate the Messages collection directly.
    private readonly DispatcherTimer _pollTimer;

    /// <summary>The default view: one row per conversation, newest activity first.</summary>
    public ObservableCollection<DmConversationViewModel> Conversations { get; } = new();

    /// <summary>Messages in the selected folder (Inbox / Sent / Deleted), newest first.</summary>
    public ObservableCollection<DmFolderItemViewModel> FolderItems { get; } = new();

    /// <summary>Mutual, approved followers — the "New message" picker, no longer the default list.</summary>
    public ObservableCollection<DmRecipient> Recipients { get; } = new();

    public ObservableCollection<DmMessageViewModel> Messages { get; } = new();

    // Images uploaded for the next DM (URLs returned by the upload endpoint).
    public ObservableCollection<string> AttachedImageUrls { get; } = new();

    [ObservableProperty]
    private DmFolder selectedFolder = DmFolder.Inbox;

    [ObservableProperty]
    private DmRecipient? selectedRecipient;

    /// <summary>True while the left rail shows the recipient picker instead of the conversation list.</summary>
    [ObservableProperty]
    private bool isPickingRecipient;

    [ObservableProperty]
    private string composeText = "";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isLoadingFolder;

    [ObservableProperty]
    private bool isSending;

    [ObservableProperty]
    private bool isUploadingImage;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Opaque cursor from the last conversations page; null when there are no more.</summary>
    [ObservableProperty]
    private string? conversationsCursor;

    /// <summary>Opaque cursor from the last folder page; null when there are no more.</summary>
    [ObservableProperty]
    private string? folderCursor;

    // Empty states must not flash before the first fetch has even been attempted
    // — "nothing here" and "not asked yet" are different things to show.
    [ObservableProperty]
    private bool hasLoadedConversationsOnce;

    [ObservableProperty]
    private bool hasLoadedFolderOnce;

    public bool HasSelection => SelectedRecipient is not null;

    /// <summary>The folder list is what the centre pane shows when no conversation is open.</summary>
    public bool ShowFolderList => SelectedRecipient is null;

    public bool HasMoreConversations => ConversationsCursor is { Length: > 0 };
    public bool HasMoreFolderItems => FolderCursor is { Length: > 0 };

    public string FolderLabel => SelectedFolder.ToLabel();
    public string FolderEmptyStateText => SelectedFolder.ToEmptyStateText();
    public string BackToFolderLabel => $"← Back to {SelectedFolder.ToLabel()}";

    /// <summary>Restore replaces Delete on rows in the Deleted tab.</summary>
    public bool IsDeletedFolder => SelectedFolder == DmFolder.Deleted;

    public bool ShowNoConversations => HasLoadedConversationsOnce && !IsLoading && Conversations.Count == 0;
    public bool ShowEmptyFolder => HasLoadedFolderOnce && !IsLoadingFolder && FolderItems.Count == 0;
    public bool ShowNoRecipients => HasLoadedConversationsOnce && !IsLoading && Recipients.Count == 0;

    public DirectMessagesViewModel(SessionService session)
    {
        _session = session;
        AttachedImageUrls.CollectionChanged += (_, _) => SendCommand.NotifyCanExecuteChanged();
        Conversations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoConversations));
        FolderItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyFolder));
        Recipients.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoRecipients));

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) => await PollThreadUpdatesAsync();
    }

    // ── Loading ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await LoadConversationsAsync();
            await LoadRecipientsAsync();
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

        await LoadFolderAsync();
    }

    /// <summary>Reload everything currently on screen (conversations + the open folder).</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await LoadConversationsAsync();
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

        await LoadFolderAsync();
    }

    private async Task LoadConversationsAsync()
    {
        var page = await _session.Api.GetDmConversationsAsync(take: PageSize);

        Conversations.Clear();
        foreach (var conversation in page.Items)
            Conversations.Add(new DmConversationViewModel(conversation));

        ConversationsCursor = page.NextCursor;
        HasLoadedConversationsOnce = true;
    }

    private async Task LoadRecipientsAsync()
    {
        var recipients = await _session.Api.GetDmRecipientsAsync();

        Recipients.Clear();
        foreach (var recipient in recipients)
            Recipients.Add(recipient);
    }

    /// <summary>Load page 1 of the selected folder.</summary>
    private async Task LoadFolderAsync()
    {
        var folder = SelectedFolder;

        IsLoadingFolder = true;
        try
        {
            var page = await _session.Api.GetDmFolderAsync(folder, take: PageSize);

            // The tab may have changed while the fetch was in flight.
            if (SelectedFolder != folder)
                return;

            var currentUserId = _session.CurrentUser?.Id;
            FolderItems.Clear();
            foreach (var message in page.Items)
                FolderItems.Add(new DmFolderItemViewModel(message, currentUserId));

            FolderCursor = page.NextCursor;
            HasLoadedFolderOnce = true;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingFolder = false;
        }
    }

    /// <summary>
    /// Switch tabs. Picking a tab also closes any open conversation, so the tab
    /// strip doubles as the way back out of a thread to its folder list.
    /// </summary>
    [RelayCommand]
    private async Task SelectFolderAsync(DmFolder folder)
    {
        var alreadyLoaded = SelectedFolder == folder && FolderItems.Count > 0;
        CloseThread();
        SelectedFolder = folder;

        if (alreadyLoaded)
            return;

        FolderCursor = null;
        await LoadFolderAsync();
    }

    // ── Cursor paging (nextCursor passed back verbatim) ────────────────────────

    [RelayCommand]
    private async Task LoadMoreConversationsAsync()
    {
        if (ConversationsCursor is not { Length: > 0 } cursor)
            return;

        IsLoading = true;
        try
        {
            var page = await _session.Api.GetDmConversationsAsync(cursor, PageSize);
            foreach (var conversation in page.Items)
                Conversations.Add(new DmConversationViewModel(conversation));

            ConversationsCursor = page.NextCursor;
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
    private async Task LoadMoreFolderItemsAsync()
    {
        if (FolderCursor is not { Length: > 0 } cursor)
            return;

        var folder = SelectedFolder;

        IsLoadingFolder = true;
        try
        {
            var page = await _session.Api.GetDmFolderAsync(folder, cursor, PageSize);

            if (SelectedFolder != folder)
                return;

            var currentUserId = _session.CurrentUser?.Id;
            foreach (var message in page.Items)
                FolderItems.Add(new DmFolderItemViewModel(message, currentUserId));

            FolderCursor = page.NextCursor;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingFolder = false;
        }
    }

    // ── Opening a conversation ────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenConversationAsync(DmConversationViewModel conversation)
    {
        if (conversation.OtherUser is not { } other)
        {
            ErrorMessage = "This conversation did not include the other participant, so it can't be opened.";
            return;
        }

        await OpenThreadAsync(other);
        // Opening a thread marks its received-unread messages read server-side,
        // so the unread counts on every conversation row are now stale.
        await ReloadConversationsQuietlyAsync();
    }

    /// <summary>
    /// Open the conversation a folder row belongs to. Because the thread GET
    /// marks the caller's unread messages read, the row is then re-fetched via
    /// GET /api/dm/{id} so its unread dot clears without reloading the folder.
    /// </summary>
    [RelayCommand]
    private async Task OpenFolderItemAsync(DmFolderItemViewModel item)
    {
        if (item.OtherUser is not { } other)
        {
            ErrorMessage = "This message did not include the other participant, so it can't be opened.";
            return;
        }

        await OpenThreadAsync(other);
        await RefreshFolderItemAsync(item);
        await ReloadConversationsQuietlyAsync();
    }

    [RelayCommand]
    private async Task SelectRecipientAsync(DmRecipient recipient)
    {
        IsPickingRecipient = false;
        await OpenThreadAsync(recipient);
    }

    private async Task OpenThreadAsync(DmRecipient other)
    {
        // Switching conversations: stop polling the old one until the new
        // thread has loaded, then restart against the new participant.
        _pollTimer.Stop();
        SelectedRecipient = other;
        await LoadThreadAsync(other);
        _pollTimer.Start();
    }

    /// <summary>Close the open conversation and go back to the folder list.</summary>
    [RelayCommand]
    private void CloseThread()
    {
        _pollTimer.Stop();
        SelectedRecipient = null;
        Messages.Clear();
        _shownMessageIds.Clear();
        ComposeText = "";
        AttachedImageUrls.Clear();
    }

    [RelayCommand]
    private void ToggleRecipientPicker() => IsPickingRecipient = !IsPickingRecipient;

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

    // ── Sending ───────────────────────────────────────────────────────────────

    private bool CanSend() =>
        HasSelection && !IsSending &&
        (!string.IsNullOrWhiteSpace(ComposeText) || AttachedImageUrls.Count > 0);

    // SendDmAsync doesn't parse its response body (see
    // InterlinedApiClient.DirectMessages.cs), so re-fetch the thread after
    // sending, and refresh the conversation list so the new activity floats up.
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
            await ReloadConversationsQuietlyAsync();
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

    // ── Trash / restore ───────────────────────────────────────────────────────
    // Both act on ONLY the caller's own copy: the other participant keeps theirs
    // and can never tell, and read state is never changed. A trashed message
    // leaves its folder for Deleted (and vice versa), so the row is dropped from
    // the list it was in and the counts are refreshed — the write endpoints
    // return an unparsed { ok: true }, hence read-after-write.

    [RelayCommand]
    private async Task TrashFolderItemAsync(DmFolderItemViewModel item)
    {
        try
        {
            await _session.Api.TrashDmAsync(item.Id);
            FolderItems.Remove(item);
            await ReloadConversationsQuietlyAsync();
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreFolderItemAsync(DmFolderItemViewModel item)
    {
        try
        {
            await _session.Api.RestoreDmAsync(item.Id);
            FolderItems.Remove(item);
            await ReloadConversationsQuietlyAsync();
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // Trash from inside an open thread: the bubble's own action. Re-fetch the
    // thread afterward, and drop the row from the folder list behind it.
    [RelayCommand]
    private async Task TrashMessageAsync(DmMessageViewModel message)
    {
        try
        {
            await _session.Api.TrashDmAsync(message.Id);
            if (SelectedRecipient is { } recipient)
                await LoadThreadAsync(recipient);
            if (FolderItems.FirstOrDefault(i => i.Id == message.Id) is { } row)
                FolderItems.Remove(row);
            await ReloadConversationsQuietlyAsync();
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
            await ReloadConversationsQuietlyAsync();
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Single-row read-after-write via GET /api/dm/{id} ──────────────────────

    /// <summary>
    /// Re-fetch exactly one message and update its row in place — used after
    /// opening a thread, which marks the caller's received messages read
    /// server-side, so only that row's unread dot needs to change.
    /// </summary>
    private async Task RefreshFolderItemAsync(DmFolderItemViewModel item)
    {
        try
        {
            var fresh = await _session.Api.GetDmAsync(item.Id);
            item.Update(fresh);
        }
        catch (InterlinedApiException)
        {
            // A 404 here just means the message is no longer reachable for this
            // caller; the next folder load will settle it. Not worth a banner.
        }
    }

    /// <summary>Refresh the conversation list without disturbing the error banner or spinner.</summary>
    private async Task ReloadConversationsQuietlyAsync()
    {
        try
        {
            await LoadConversationsAsync();
        }
        catch (InterlinedApiException)
        {
            // Stale unread counts are not worth surfacing as an error.
        }
    }

    // ── Polling an open thread ───────────────────────────────────────────────

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

    // ── Property change fan-out ──────────────────────────────────────────────

    partial void OnSelectedRecipientChanged(DmRecipient? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowFolderList));
        SendCommand.NotifyCanExecuteChanged();
        // No selection → nothing to poll.
        if (value is null)
            _pollTimer.Stop();
    }

    partial void OnSelectedFolderChanged(DmFolder value)
    {
        OnPropertyChanged(nameof(FolderLabel));
        OnPropertyChanged(nameof(FolderEmptyStateText));
        OnPropertyChanged(nameof(BackToFolderLabel));
        OnPropertyChanged(nameof(IsDeletedFolder));
    }

    partial void OnConversationsCursorChanged(string? value) => OnPropertyChanged(nameof(HasMoreConversations));
    partial void OnFolderCursorChanged(string? value) => OnPropertyChanged(nameof(HasMoreFolderItems));
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoConversations));
        OnPropertyChanged(nameof(ShowNoRecipients));
    }

    partial void OnHasLoadedConversationsOnceChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoConversations));
        OnPropertyChanged(nameof(ShowNoRecipients));
    }

    partial void OnHasLoadedFolderOnceChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyFolder));

    partial void OnIsLoadingFolderChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyFolder));
    partial void OnComposeTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}
