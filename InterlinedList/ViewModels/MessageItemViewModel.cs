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

    public IReadOnlyList<string> VideoUrls { get; }
    public bool HasVideos => VideoUrls.Count > 0;

    /// <summary>
    /// Freeform tags on this message. Rendered as clickable chips; the click is
    /// routed to the feed's tag filter, not handled here (the card has no view of
    /// the feed's paging state).
    /// </summary>
    public IReadOnlyList<string> Tags { get; }
    public bool HasTags => Tags.Count > 0;

    public ObservableCollection<MessageItemViewModel> Replies { get; } = new();

    // ── Push / Quote ────────────────────────────────────────────────────────────

    /// <summary>This card is itself a Push or a Quote of another message.</summary>
    public bool IsPushOrQuote { get; }

    /// <summary>A bare repost: pushed, with no commentary of its own.</summary>
    public bool IsPush { get; }

    /// <summary>A push that added a note. The API has one field for both.</summary>
    public bool IsQuote { get; }

    /// <summary>Badge above the header row; null on an ordinary post.</summary>
    public string? RepostLabel => IsQuote ? "❝ Quoted" : IsPush ? "↻ Pushed" : null;

    /// <summary>A bare push has no body text of its own — collapse the body block.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    /// <summary>
    /// What a Push/Quote from this card re-shares. Pushing a <i>bare push</i>
    /// targets the original instead of the push, so the result is a one-level
    /// quote we can actually render — no chain of empty cards. Quoting a Quote
    /// still targets the quote, because a quote carries its own commentary.
    /// Client-side choice: no nested <c>pushedMessage</c> appeared in the sampled
    /// feed, so the server's own chaining behaviour is unverified.
    /// </summary>
    public string PushTargetId { get; }

    public bool HasQuotedOriginal { get; }
    public string? QuotedAuthorDisplayName { get; }
    public string? QuotedAuthorHandle { get; }
    public string? QuotedAuthorUsername { get; }
    public string? QuotedAvatarUrl { get; }
    public string? QuotedContent { get; }
    public string? QuotedTimeFormatted { get; }
    public IReadOnlyList<string> QuotedImageUrls { get; }
    public bool QuotedHasImages => QuotedImageUrls.Count > 0;

    /// <summary>
    /// Raised after this card publishes something (a Push or a Quote) so the feed
    /// that owns it can re-fetch — the write endpoints' response envelopes aren't
    /// parsed, per the codebase's read-after-write discipline.
    /// </summary>
    public event EventHandler? Posted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private string content;

    [ObservableProperty]
    private int digCount;

    [ObservableProperty]
    private int pushCount;

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
    private bool isComposingQuote;

    [ObservableProperty]
    private string quoteText = "";

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
        VideoUrls = message.VideoUrls ?? new List<string>();
        Tags = message.Tags ?? new List<string>();

        digCount = message.DigCount;
        pushCount = message.PushCount;
        dugByMe = message.DugByMe;

        IsPushOrQuote = message.IsPushOrQuote;
        IsQuote = message.IsQuote;
        IsPush = message.IsPushOrQuote && !message.IsQuote;
        PushTargetId = IsPush ? message.PushedMessageId! : message.Id;

        var original = message.PushedMessage;
        HasQuotedOriginal = original is not null;
        QuotedAuthorDisplayName = original?.AuthorDisplayName;
        QuotedAuthorHandle = original?.AuthorHandle;
        QuotedAuthorUsername = original?.User?.Username;
        QuotedAvatarUrl = original?.User?.Avatar;
        QuotedContent = original?.Content;
        QuotedTimeFormatted = original?.TimeFormatted;
        QuotedImageUrls = original?.ImageUrls ?? new List<string>();
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

    // ── Push (repost) / Quote ───────────────────────────────────────────────────

    private bool CanPush() => !IsBusy;

    /// <summary>
    /// Push (repost) as-is — no comment, always public. Posts directly, per the
    /// product behaviour in <c>/help/messages</c>; Quote is the path that asks
    /// for confirmation first (it has a composer to show the banner in).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync()
    {
        IsBusy = true;
        PushCount++;
        try
        {
            await _api.PushMessageAsync(PushTargetId);
            ErrorMessage = null;
            Posted?.Invoke(this, EventArgs.Empty);
        }
        catch (InterlinedApiException ex)
        {
            PushCount--;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartQuote() => IsComposingQuote = true;

    [RelayCommand]
    private void CancelQuote()
    {
        IsComposingQuote = false;
        QuoteText = "";
    }

    private bool CanPostQuote() => !IsBusy && !string.IsNullOrWhiteSpace(QuoteText);

    /// <summary>
    /// Quote = push + commentary. Forced public: the composer shows the "always
    /// public" banner while this is open, which is the confirmation the product
    /// calls for.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPostQuote))]
    private async Task PostQuoteAsync()
    {
        IsBusy = true;
        try
        {
            await _api.QuoteMessageAsync(PushTargetId, QuoteText.Trim());
            QuoteText = "";
            IsComposingQuote = false;
            PushCount++;
            ErrorMessage = null;
            Posted?.Invoke(this, EventArgs.Empty);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenQuotedAuthor()
    {
        if (!string.IsNullOrEmpty(QuotedAuthorUsername))
            Navigator.OpenProfile(QuotedAuthorUsername);
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

    [RelayCommand]
    private void OpenVideo(string url)
    {
        if (!string.IsNullOrEmpty(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    partial void OnEditTextChanged(string value) => SaveEditCommand.NotifyCanExecuteChanged();
    partial void OnReplyTextChanged(string value) => PostReplyCommand.NotifyCanExecuteChanged();
    partial void OnQuoteTextChanged(string value) => PostQuoteCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        PushCommand.NotifyCanExecuteChanged();
        PostQuoteCommand.NotifyCanExecuteChanged();
    }
}
