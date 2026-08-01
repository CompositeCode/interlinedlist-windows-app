using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class ProfileSummaryViewModel : ObservableObject
{
    private readonly SessionService _session;

    public string DisplayName => _session.CurrentUser?.DisplayNameOrUsername ?? "";
    public string Username => _session.CurrentUser?.Username ?? "";
    public string? Avatar => _session.CurrentUser?.Avatar;
    public string? Bio => _session.CurrentUser?.Bio;

    [ObservableProperty]
    private int followers;

    [ObservableProperty]
    private int following;

    [ObservableProperty]
    private string? errorMessage;

    public event EventHandler? LoggedOut;

    public ProfileSummaryViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var currentUser = _session.CurrentUser;
        if (currentUser is null) return;

        try
        {
            var counts = await _session.Api.GetFollowCountsAsync(currentUser.Id);
            Followers = counts.Followers;
            Following = counts.Following;
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _session.Logout();
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }
}
