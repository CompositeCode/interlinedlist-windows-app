using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;
using Microsoft.Win32;

namespace InterlinedList.ViewModels;

public partial class FeedViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly SessionService _session;
    private int _offset;

    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private bool hasMore;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string composeText = "";

    [ObservableProperty]
    private bool isPosting;

    // Which providers are actually linked (from GET /api/user/identities) —
    // only show a cross-post toggle for a provider the user can post to.
    [ObservableProperty]
    private bool isBlueskyLinked;

    [ObservableProperty]
    private bool isTwitterLinked;

    [ObservableProperty]
    private string? mastodonProvider;

    [ObservableProperty]
    private bool crossPostToBluesky;

    [ObservableProperty]
    private bool crossPostToTwitter;

    [ObservableProperty]
    private bool crossPostToMastodon;

    // Media uploaded for the next post (URLs returned by the upload endpoints).
    public ObservableCollection<string> AttachedImageUrls { get; } = new();
    public ObservableCollection<string> AttachedVideoUrls { get; } = new();

    [ObservableProperty]
    private bool isUploadingImage;

    [ObservableProperty]
    private bool isUploadingVideo;

    // Scheduling: when IsScheduling, ScheduleDate + ScheduleTime ("HH:mm") set scheduledAt.
    [ObservableProperty]
    private bool isScheduling;

    [ObservableProperty]
    private DateTime scheduleDate = DateTime.Today;

    [ObservableProperty]
    private string scheduleTime = "09:00";

    // "Scheduled posts" panel (loaded on demand, toggled from the header).
    public ObservableCollection<MessageItemViewModel> ScheduledMessages { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduledToggleLabel))]
    private bool showScheduled;

    public string ScheduledToggleLabel => ShowScheduled ? "← Back to feed" : "Scheduled";

    // ── Tags ────────────────────────────────────────────────────────────────────

    /// <summary>Tags attached to the next post, sent as <c>tags: string[]</c>.</summary>
    public ObservableCollection<string> ComposeTags { get; } = new();

    /// <summary>Prefix-match suggestions for whatever is in <see cref="TagInput"/>.</summary>
    public ObservableCollection<TrendingTag> TagSuggestions { get; } = new();

    /// <summary>Most-used tags in the server's trailing window.</summary>
    public ObservableCollection<TrendingTag> TrendingTags { get; } = new();

    [ObservableProperty]
    private string tagInput = "";

    public bool HasTrendingTags => TrendingTags.Count > 0;

    /// <summary>
    /// The tag the feed is currently filtered to (<c>GET /api/messages?tag=</c>),
    /// or null for the unfiltered feed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTag))]
    [NotifyPropertyChangedFor(nameof(ActiveTagLabel))]
    private string? activeTag;

    public bool HasActiveTag => !string.IsNullOrEmpty(ActiveTag);
    public string ActiveTagLabel => $"Showing #{ActiveTag}";

    // Debounces the autocomplete call so typing doesn't fire one request per
    // keystroke. Cancelled and replaced on each change; never awaited on the
    // dispatcher.
    private CancellationTokenSource? _autocompleteCts;

    // ── Link previews ───────────────────────────────────────────────────────────

    /// <summary>
    /// The viewer's <c>showPreviews</c> preference from <c>GET /api/user</c>. When
    /// off, no preview card renders anywhere in the feed — not on cards, not in the
    /// composer. It is <c>false</c> on the test account, so the off path is the one
    /// that actually got looked at.
    /// </summary>
    public bool ShowPreviews => _session.CurrentUser?.ShowPreviews ?? false;

    /// <summary>
    /// Ad-hoc unfurl of the first URL in the draft
    /// (<c>GET /api/link-metadata?url=</c>), so the composer shows what the post
    /// will look like. Null when there's no URL yet, the unfurl failed, or
    /// previews are switched off.
    /// </summary>
    [ObservableProperty]
    private LinkPreviewViewModel? composePreview;

    // Same debounce discipline as the tag autocomplete.
    private CancellationTokenSource? _composePreviewCts;
    private string? _composePreviewUrl;

    /// <summary>
    /// First http(s) URL in a draft. Trailing punctuation is trimmed — people
    /// write "see https://example.com/x." and the sentence period is not part of
    /// the link.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UrlPattern =
        new(@"https?://[^\s<>""]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public FeedViewModel(SessionService session)
    {
        _session = session;
        AttachedImageUrls.CollectionChanged += (_, _) => PostCommand.NotifyCanExecuteChanged();
        AttachedVideoUrls.CollectionChanged += (_, _) => PostCommand.NotifyCanExecuteChanged();
        TrendingTags.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTrendingTags));
    }

    /// <summary>
    /// Wrap a wire message for the feed. Centralized so every card is hooked to
    /// <see cref="MessageItemViewModel.Posted"/> — a Push or Quote publishes a new
    /// post, and the write response isn't parsed, so the feed re-fetches.
    /// </summary>
    private MessageItemViewModel Wrap(Message message)
    {
        var item = new MessageItemViewModel(message, _session.Api, _session.CurrentUser?.Id, ShowPreviews);
        item.Posted += OnItemPosted;
        return item;
    }

    private void OnItemPosted(object? sender, EventArgs e) => RefreshCommand.Execute(null);

    // ── Tag commands ────────────────────────────────────────────────────────────

    /// <summary>
    /// Add whatever is typed to the compose tag list. Splits on commas only —
    /// live tags contain spaces (<c>orbit culture</c>,
    /// <c>life is short, o brave girl</c>), so whitespace can't be a delimiter.
    /// Duplicates are ignored, case-insensitively.
    /// </summary>
    private bool CanAddTag() => !string.IsNullOrWhiteSpace(TagInput);

    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private void AddTag()
    {
        foreach (var part in TagInput.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            AddTagValue(part);

        TagInput = "";
        TagSuggestions.Clear();
    }

    /// <summary>Accept an autocomplete or trending suggestion into the composer.</summary>
    [RelayCommand]
    private void UseTagSuggestion(string? tag)
    {
        AddTagValue(tag);
        TagInput = "";
        TagSuggestions.Clear();
    }

    private void AddTagValue(string? tag)
    {
        var value = tag?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        if (ComposeTags.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase))) return;
        ComposeTags.Add(value);
    }

    [RelayCommand]
    private void RemoveTag(string tag) => ComposeTags.Remove(tag);

    /// <summary>Filter the feed to one tag. Called from a chip on a card or from the trending panel.</summary>
    [RelayCommand]
    private async Task FilterByTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        ActiveTag = tag.Trim();
        ShowScheduled = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearTagFilterAsync()
    {
        if (ActiveTag is null) return;
        ActiveTag = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task LoadTrendingTagsAsync()
    {
        try
        {
            var trending = await _session.Api.GetTrendingTagsAsync();
            TrendingTags.Clear();
            foreach (var t in trending)
                TrendingTags.Add(t);
        }
        catch (InterlinedApiException)
        {
            // Decorative panel — a failure here shouldn't put an error banner
            // over the feed the user actually came for.
        }
    }

    partial void OnTagInputChanged(string value)
    {
        AddTagCommand.NotifyCanExecuteChanged();
        _ = SuggestTagsAsync(value);
    }

    // ── Compose-time link preview ───────────────────────────────────────────────

    private static string? FirstUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = UrlPattern.Match(text);
        if (!match.Success) return null;

        var url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'');
        return url.Length > "https://".Length ? url : null;
    }

    /// <summary>
    /// Debounced ad-hoc unfurl of the draft's first URL. Fire-and-forget, like the
    /// tag autocomplete: it must not block the dispatcher, and a keystroke that
    /// supersedes an in-flight request cancels it.
    /// </summary>
    private async Task UpdateComposePreviewAsync(string? text)
    {
        if (!ShowPreviews)
        {
            ComposePreview = null;
            return;
        }

        var url = FirstUrl(text);
        if (url is null)
        {
            _composePreviewCts?.Cancel();
            _composePreviewUrl = null;
            ComposePreview = null;
            return;
        }

        // Still the same link — don't re-unfurl on every character of prose typed
        // after it.
        if (string.Equals(url, _composePreviewUrl, StringComparison.OrdinalIgnoreCase)) return;

        _composePreviewCts?.Cancel();
        _composePreviewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _composePreviewCts = cts;
        _composePreviewUrl = url;

        try
        {
            await Task.Delay(600, cts.Token);
            var preview = await _session.Api.GetLinkMetadataAsync(url, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            // A failed unfurl comes back as null, not as an exception — no card.
            ComposePreview = preview is null ? null : new LinkPreviewViewModel(preview);
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch (InterlinedApiException)
        {
            ComposePreview = null;
        }
    }

    /// <summary>
    /// Debounced prefix autocomplete. Fire-and-forget on purpose: it must never
    /// block the dispatcher, and a superseded keystroke's request is cancelled
    /// rather than awaited.
    /// </summary>
    private async Task SuggestTagsAsync(string prefix)
    {
        _autocompleteCts?.Cancel();
        _autocompleteCts?.Dispose();

        if (string.IsNullOrWhiteSpace(prefix))
        {
            _autocompleteCts = null;
            TagSuggestions.Clear();
            return;
        }

        var cts = new CancellationTokenSource();
        _autocompleteCts = cts;
        try
        {
            await Task.Delay(250, cts.Token);
            var matches = await _session.Api.AutocompleteTagsAsync(prefix, ct: cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            TagSuggestions.Clear();
            foreach (var m in matches)
            {
                if (ComposeTags.Any(t => string.Equals(t, m.Tag, StringComparison.OrdinalIgnoreCase))) continue;
                TagSuggestions.Add(m);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke.
        }
        catch (InterlinedApiException)
        {
            TagSuggestions.Clear();
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
            var url = await _session.Api.UploadMessageImageAsync(stream, Path.GetFileName(dlg.FileName), contentType);
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

    [RelayCommand]
    private async Task AttachVideoAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Videos (*.mp4;*.mov;*.webm;*.m4v)|*.mp4;*.mov;*.webm;*.m4v",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        IsUploadingVideo = true;
        try
        {
            var contentType = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
            {
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".m4v" => "video/x-m4v",
                _ => "video/mp4",
            };
            await using var stream = File.OpenRead(dlg.FileName);
            var url = await _session.Api.UploadMessageVideoAsync(stream, Path.GetFileName(dlg.FileName), contentType);
            AttachedVideoUrls.Add(url);
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
            IsUploadingVideo = false;
        }
    }

    [RelayCommand]
    private void RemoveVideoAttachment(string url) => AttachedVideoUrls.Remove(url);

    [RelayCommand]
    private async Task ToggleScheduledAsync()
    {
        ShowScheduled = !ShowScheduled;
        if (ShowScheduled)
            await LoadScheduledAsync();
    }

    private async Task LoadScheduledAsync()
    {
        try
        {
            var scheduled = await _session.Api.GetScheduledMessagesAsync();
            ScheduledMessages.Clear();
            foreach (var m in scheduled)
                ScheduledMessages.Add(Wrap(m));
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadCrossPostOptionsAsync()
    {
        try
        {
            var identities = await _session.Api.GetLinkedIdentitiesAsync();
            IsBlueskyLinked = identities.Any(i => i.Provider == "bluesky");
            IsTwitterLinked = identities.Any(i => i.Provider == "twitter");
            MastodonProvider = identities.FirstOrDefault(i => i.Provider.StartsWith("mastodon", StringComparison.Ordinal))?.Provider;
        }
        catch (InterlinedApiException)
        {
            // Non-critical for the feed itself — cross-post toggles just stay hidden/disabled.
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            _offset = 0;
            var page = await _session.Api.GetFeedPageAsync(limit: PageSize, offset: 0, tag: ActiveTag);

            Messages.Clear();
            foreach (var message in page.Messages)
                Messages.Add(Wrap(message));

            HasMore = page.Pagination.HasMore;
            _offset = page.Messages.Count;
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

    private bool CanLoadMore() => HasMore && !IsLoading && !IsLoadingMore;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        IsLoadingMore = true;
        try
        {
            var page = await _session.Api.GetFeedPageAsync(limit: PageSize, offset: _offset, tag: ActiveTag);

            foreach (var message in page.Messages)
                Messages.Add(Wrap(message));

            _offset += page.Messages.Count;
            HasMore = page.Pagination.HasMore;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private bool CanPost() => !IsPosting &&
        (!string.IsNullOrWhiteSpace(ComposeText) || AttachedImageUrls.Count > 0 || AttachedVideoUrls.Count > 0);

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        IsPosting = true;
        try
        {
            await _session.Api.PostMessageAsync(new NewMessage
            {
                Content = ComposeText.Trim(),
                PubliclyVisible = _session.CurrentUser?.DefaultPubliclyVisible ?? true,
                CrossPostToBluesky = CrossPostToBluesky,
                CrossPostToTwitter = CrossPostToTwitter,
                MastodonProviderIds = CrossPostToMastodon ? MastodonProvider : null,
                ScheduledAt = ResolveScheduledAt(),
                ImageUrls = AttachedImageUrls.Count > 0 ? AttachedImageUrls.ToList() : null,
                VideoUrls = AttachedVideoUrls.Count > 0 ? AttachedVideoUrls.ToList() : null,
                Tags = ComposeTags.Count > 0 ? ComposeTags.ToList() : null,
            });
            ComposeText = "";
            ComposeTags.Clear();
            TagInput = "";
            TagSuggestions.Clear();
            CrossPostToBluesky = false;
            CrossPostToTwitter = false;
            CrossPostToMastodon = false;
            AttachedImageUrls.Clear();
            AttachedVideoUrls.Clear();
            var wasScheduled = IsScheduling;
            IsScheduling = false;
            // A scheduled post won't appear in the live feed — refresh the
            // scheduled panel instead so the user sees it land.
            if (wasScheduled)
            {
                await LoadScheduledAsync();
                ShowScheduled = true;
            }
            else
            {
                await RefreshAsync();
            }
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsPosting = false;
        }
    }

    private DateTimeOffset? ResolveScheduledAt()
    {
        if (!IsScheduling) return null;
        var time = TimeSpan.TryParse(ScheduleTime, out var t) ? t : new TimeSpan(9, 0, 0);
        var local = ScheduleDate.Date + time;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    partial void OnHasMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsPostingChanged(bool value) => PostCommand.NotifyCanExecuteChanged();
    partial void OnComposeTextChanged(string value)
    {
        PostCommand.NotifyCanExecuteChanged();
        _ = UpdateComposePreviewAsync(value);
    }
}
