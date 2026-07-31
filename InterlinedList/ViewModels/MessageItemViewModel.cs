using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Wraps a single Message for display/interaction in the feed. DigCount/DugByMe
/// are mutable, observable copies (the underlying Message is immutable) so the
/// Dig command can update them optimistically. Content is observable too, so an
/// in-place edit reflects immediately. Edit/Delete/Report/Reply follow the
/// codebase's read-after-write discipline — the server write is fire-then-trust
/// for text (edit) or re-fetch (replies).
/// </summary>
public partial class MessageItemViewModel : ObservableObject
{
    private readonly InterlinedApiClient _api;
    private readonly string? _currentUserId;
    private readonly bool _publiclyVisible;

    public string Id { get; }
    public string TimeFormatted { get; }
    public string AuthorDisplayName { get; }
    public string AuthorHandle { get; }
    public string? AuthorUsername { get; }
    public string? AvatarUrl { get; }
    public bool IsMine { get; }
    public bool CanReport => !IsMine;

    public IReadOnlyList<string> ImageUrls { get; }
    public bool HasImages => ImageUrls.Count > 0;

    public ObservableCollection<MessageItemViewModel> Replies { get; } = new();

    [ObservableProperty]
    private string content;

    [ObservableProperty]
    private int digCount;

    [ObservableProperty]
    private bool dugByMe;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isDeleted;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editText = "";

    [ObservableProperty]
    private bool areRepliesVisible;

    [ObservableProperty]
    private bool isComposingReply;

    [ObservableProperty]
    private string replyText = "";

    [ObservableProperty]
    private string? errorMessage;

    public MessageItemViewModel(Message message, InterlinedApiClient api, string? currentUserId)
    {
        _api = api;
        _currentUserId = currentUserId;
        _publiclyVisible = message.PubliclyVisible;

        Id = message.Id;
        content = message.Content;
        TimeFormatted = message.TimeFormatted;
        AuthorDisplayName = message.AuthorDisplayName;
        AuthorHandle = message.AuthorHandle;
        AuthorUsername = message.User?.Username;
        AvatarUrl = message.User?.Avatar;
        IsMine = message.UserId == currentUserId;
        ImageUrls = message.ImageUrls ?? new List<string>();

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

    // ── Edit (own message) ──────────────────────────────────────────────────────

    [RelayCommand]
    private void StartEdit()
    {
        EditText = Content;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    private bool CanSaveEdit() => !string.IsNullOrWhiteSpace(EditText);

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync()
    {
        try
        {
            var text = EditText.Trim();
            await _api.EditMessageAsync(Id, text);
            Content = text;
            IsEditing = false;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Delete (own message) ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task DeleteAsync()
    {
        try
        {
            await _api.DeleteMessageAsync(Id);
            IsDeleted = true; // the card collapses via a DataTrigger
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Report (someone else's message) ─────────────────────────────────────────

    [RelayCommand]
    private async Task ReportAsync()
    {
        try
        {
            await _api.ReportMessageAsync(Id, "other", null);
            ErrorMessage = "Reported. Thanks — our team will take a look.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Replies / thread ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleRepliesAsync()
    {
        if (AreRepliesVisible)
        {
            AreRepliesVisible = false;
            return;
        }

        try
        {
            var replies = await _api.GetRepliesAsync(Id);
            Replies.Clear();
            foreach (var reply in replies)
                Replies.Add(new MessageItemViewModel(reply, _api, _currentUserId));
            AreRepliesVisible = true;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void StartReply() => IsComposingReply = true;

    [RelayCommand]
    private void CancelReply()
    {
        IsComposingReply = false;
        ReplyText = "";
    }

    private bool CanPostReply() => !string.IsNullOrWhiteSpace(ReplyText);

    [RelayCommand(CanExecute = nameof(CanPostReply))]
    private async Task PostReplyAsync()
    {
        try
        {
            await _api.PostReplyAsync(Id, ReplyText.Trim(), _publiclyVisible);
            ReplyText = "";
            IsComposingReply = false;

            var replies = await _api.GetRepliesAsync(Id);
            Replies.Clear();
            foreach (var reply in replies)
                Replies.Add(new MessageItemViewModel(reply, _api, _currentUserId));
            AreRepliesVisible = true;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenAuthor()
    {
        if (!string.IsNullOrEmpty(AuthorUsername))
            Navigator.OpenProfile(AuthorUsername);
    }

    partial void OnEditTextChanged(string value) => SaveEditCommand.NotifyCanExecuteChanged();
    partial void OnReplyTextChanged(string value) => PostReplyCommand.NotifyCanExecuteChanged();
}
