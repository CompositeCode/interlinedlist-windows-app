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
    private string? errorMessage;

    public bool HasRequests => FollowRequests.Count > 0;

    public bool HasProfile => Profile is not null;

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
        var username = LookupUsername.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(username))
            return;

        IsLoading = true;
        try
        {
            var profile = await _session.Api.GetProfileAsync(username);
            Profile = profile;

            Relationship = await _session.Api.GetFollowStatusAsync(profile.Id);
            IsBlocking = await _session.Api.IsBlockingAsync(profile.Username);
            IsMuting = await _session.Api.IsMutingAsync(profile.Username);

            var page = await _session.Api.GetUserMessagesAsync(profile.Username);
            Messages.Clear();
            foreach (var message in page.Messages)
                Messages.Add(new MessageItemViewModel(message, _session.Api, _session.CurrentUser?.Id));

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
}
