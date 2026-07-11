using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

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

    public FeedViewModel(SessionService session)
    {
        _session = session;
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
            var page = await _session.Api.GetMessagesAsync(limit: PageSize, offset: 0);

            Messages.Clear();
            foreach (var message in page.Messages)
                Messages.Add(new MessageItemViewModel(message, _session.Api, _session.CurrentUser?.Id));

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
            var page = await _session.Api.GetMessagesAsync(limit: PageSize, offset: _offset);

            foreach (var message in page.Messages)
                Messages.Add(new MessageItemViewModel(message, _session.Api, _session.CurrentUser?.Id));

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

    private bool CanPost() => !IsPosting && !string.IsNullOrWhiteSpace(ComposeText);

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        IsPosting = true;
        try
        {
            await _session.Api.PostMessageAsync(
                ComposeText.Trim(),
                _session.CurrentUser?.DefaultPubliclyVisible ?? true,
                crossPostToBluesky: CrossPostToBluesky,
                crossPostToTwitter: CrossPostToTwitter,
                mastodonProviderIds: CrossPostToMastodon ? MastodonProvider : null);
            ComposeText = "";
            CrossPostToBluesky = false;
            CrossPostToTwitter = false;
            CrossPostToMastodon = false;
            await RefreshAsync();
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

    partial void OnHasMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();
    partial void OnIsPostingChanged(bool value) => PostCommand.NotifyCanExecuteChanged();
    partial void OnComposeTextChanged(string value) => PostCommand.NotifyCanExecuteChanged();
}
