using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Backs the People view: look up any user's public profile, follow/unfollow
/// them, browse their recent messages, and approve/reject the follow requests
/// pending on the current (private) account.
/// </summary>
public partial class ProfileViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<FollowUser> FollowRequests { get; } = new();
    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
    /// <summary>
    /// Mutual-follow counts. The API exposes counts only — there is no endpoint
    /// listing the mutual users, so the old clickable chips could never work
    /// (#160).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMutuals))]
    [NotifyPropertyChangedFor(nameof(MutualsSummary))]
    private MutualFollowCounts? mutuals;

    /// <summary>The profile's followers, paged. Server default page is 50.</summary>
    public ObservableCollection<FollowUser> Followers { get; } = new();

    /// <summary>Who the profile follows, paged.</summary>
    public ObservableCollection<FollowUser> Following { get; } = new();

    private const int FollowPageSize = 25;

    [ObservableProperty]
    private string lookupUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    private UserProfile? profile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowButtonText))]
    private FollowStatus? relationship;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlockButtonText))]
    private bool isBlocking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteButtonText))]
    private bool isMuting;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowersHeader))]
    private int followersTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowingHeader))]
    private int followingTotal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreFollowersCommand))]
    private bool hasMoreFollowers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreFollowingCommand))]
    private bool hasMoreFollowing;

    [ObservableProperty]
    private bool isLoadingFollowLists;

    /// <summary>
    /// Removing a follower is only meaningful on your own profile — the endpoint
    /// severs an edge pointing at you.
    /// </summary>
    [ObservableProperty]
    private bool isOwnProfile;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasRequests => FollowRequests.Count > 0;

    public bool HasProfile => Profile is not null;

    public bool HasMutuals => Mutuals?.HasAny == true;

    public string MutualsSummary => Mutuals?.Summary ?? string.Empty;

    public string FollowButtonText =>
        Relationship?.IsFollowing == true ? "Following"
        : Relationship?.IsPending == true ? "Requested"
        : "Follow";

    public string BlockButtonText => IsBlocking ? "Unblock" : "Block";

    public string MuteButtonText => IsMuting ? "Unmute" : "Mute";

    public ProfileViewModel(SessionService session)
    {
        _session = session;
        FollowRequests.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRequests));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var requests = await _session.Api.GetFollowRequestsAsync();

            FollowRequests.Clear();
            foreach (var request in requests)
                FollowRequests.Add(request);

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
    private async Task LoadProfileAsync()
    {
        var typed = LookupUsername.Trim();
        if (string.IsNullOrWhiteSpace(typed))
            return;

        IsLoading = true;
        try
        {
            // Resolve through the lookup endpoint first. It accepts a bare
            // username only — a pasted "@adron" 404s — so this both strips the
            // "@" users naturally type and turns "no such account" into a clear
            // message instead of a raw 404 from the profile fetch.
            var resolved = await _session.Api.LookupUserAsync(typed);
            if (resolved is null)
            {
                Profile = null;
                ErrorMessage = $"No account found for \u201c{typed}\u201d.";
                return;
            }

            var profile = await _session.Api.GetProfileAsync(resolved.Username);
            Profile = profile;

            Relationship = await _session.Api.GetFollowStatusAsync(profile.Id);
            IsBlocking = await _session.Api.IsBlockingAsync(profile.Username);
            IsMuting = await _session.Api.IsMutingAsync(profile.Username);

            var page = await _session.Api.GetUserMessagesAsync(profile.Username);
            Messages.Clear();
            foreach (var message in page.Messages)
                Messages.Add(new MessageItemViewModel(message, _session.Api, _session.CurrentUser?.Id));

            Mutuals = await _session.Api.GetMutualCountsAsync(profile.Id);

            IsOwnProfile = profile.Id == _session.CurrentUser?.Id;

            Followers.Clear();
            Following.Clear();
            await LoadMoreFollowersAsync();
            await LoadMoreFollowingAsync();

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
    private async Task ToggleFollowAsync()
    {
        if (Profile is null)
            return;

        try
        {
            if (Relationship?.IsFollowing == true)
                await _session.Api.UnfollowAsync(Profile.Id);
            else
                await _session.Api.FollowAsync(Profile.Id);

            // Write path isn't trusted for its response shape — read back the
            // relationship and refresh the profile so the counts stay honest.
            Relationship = await _session.Api.GetFollowStatusAsync(Profile.Id);
            Profile = await _session.Api.GetProfileAsync(Profile.Username);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleBlockAsync()
    {
        if (Profile is null) return;
        try
        {
            if (IsBlocking) await _session.Api.UnblockUserAsync(Profile.Username);
            else await _session.Api.BlockUserAsync(Profile.Username);
            IsBlocking = await _session.Api.IsBlockingAsync(Profile.Username);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        if (Profile is null) return;
        try
        {
            if (IsMuting) await _session.Api.UnmuteUserAsync(Profile.Username);
            else await _session.Api.MuteUserAsync(Profile.Username);
            IsMuting = await _session.Api.IsMutingAsync(Profile.Username);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ReportAsync()
    {
        if (Profile is null) return;
        try
        {
            await _session.Api.ReportUserAsync(Profile.Username, "other", null);
            ErrorMessage = "Reported. Thanks — our team will take a look.";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenUserAsync(FollowUser user)
    {
        LookupUsername = user.Username;
        await LoadProfileAsync();
    }

    [RelayCommand]
    private async Task ApproveAsync(FollowUser user)
    {
        try
        {
            await _session.Api.ApproveFollowAsync(user.Id);
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RejectAsync(FollowUser user)
    {
        try
        {
            await _session.Api.RejectFollowAsync(user.Id);
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
    // ── Followers / following ───────────────────────────────────────────────
    // Paged rather than one-shot: the server's default page is 50, so a user
    // with more than that had the rest silently dropped. `total` comes from the
    // pagination block and is shown in the header so the count is honest even
    // before everything is loaded.

    public string FollowersHeader => FollowersTotal > 0
        ? $"FOLLOWERS ({Followers.Count} of {FollowersTotal})"
        : "FOLLOWERS";

    public string FollowingHeader => FollowingTotal > 0
        ? $"FOLLOWING ({Following.Count} of {FollowingTotal})"
        : "FOLLOWING";

    [RelayCommand(CanExecute = nameof(CanLoadMoreFollowers))]
    private async Task LoadMoreFollowersAsync()
    {
        if (Profile is null) return;

        IsLoadingFollowLists = true;
        try
        {
            var page = await _session.Api.GetFollowersPageAsync(
                Profile.Id, FollowPageSize, Followers.Count);

            foreach (var user in page.Users)
                Followers.Add(user);

            FollowersTotal = page.Total;
            HasMoreFollowers = page.HasMore;
            OnPropertyChanged(nameof(FollowersHeader));
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingFollowLists = false;
        }
    }

    private bool CanLoadMoreFollowers() => HasMoreFollowers;

    [RelayCommand(CanExecute = nameof(CanLoadMoreFollowing))]
    private async Task LoadMoreFollowingAsync()
    {
        if (Profile is null) return;

        IsLoadingFollowLists = true;
        try
        {
            var page = await _session.Api.GetFollowingPageAsync(
                Profile.Id, FollowPageSize, Following.Count);

            foreach (var user in page.Users)
                Following.Add(user);

            FollowingTotal = page.Total;
            HasMoreFollowing = page.HasMore;
            OnPropertyChanged(nameof(FollowingHeader));
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingFollowLists = false;
        }
    }

    private bool CanLoadMoreFollowing() => HasMoreFollowing;

    /// <summary>
    /// Sever a follower's edge to you. Two-step: <see cref="PendingRemoval"/>
    /// arms it and the second press confirms, so an irreversible action isn't a
    /// single mis-click — and without a modal, which would block the dispatcher.
    /// </summary>
    [ObservableProperty]
    private FollowUser? pendingRemoval;

    [RelayCommand]
    private void ArmRemoveFollower(FollowUser follower) => PendingRemoval = follower;

    [RelayCommand]
    private void CancelRemoveFollower() => PendingRemoval = null;

    [RelayCommand]
    private async Task RemoveFollowerAsync(FollowUser follower)
    {
        if (follower is null || Profile is null) return;

        try
        {
            await _session.Api.RemoveFollowerAsync(follower.Id);
            PendingRemoval = null;

            // Read-after-write: re-page from the start rather than mutating the
            // local list, so the total and hasMore stay truthful.
            Followers.Clear();
            HasMoreFollowers = true;
            await LoadMoreFollowersAsync();
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
