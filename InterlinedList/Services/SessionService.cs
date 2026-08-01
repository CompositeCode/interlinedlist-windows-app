using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Owns the logged-in state: restoring a saved session at launch, logging in,
/// and logging out. ViewModels bind to CurrentUser/IsAuthenticated directly.
/// </summary>
public sealed partial class SessionService : ObservableObject
{
    private readonly InterlinedApiClient _api;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticated))]
    private CurrentUser? currentUser;

    public bool IsAuthenticated => CurrentUser is not null;

    public InterlinedApiClient Api => _api;

    public SessionService(InterlinedApiClient api)
    {
        _api = api;
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        var token = CredentialStore.LoadToken();
        if (token is null) return false;

        _api.AccessToken = token;
        try
        {
            CurrentUser = await _api.GetCurrentUserAsync(ct);
            return true;
        }
        catch (InterlinedApiException)
        {
            _api.AccessToken = null;
            CredentialStore.ClearToken();
            return false;
        }
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var token = await _api.RequestSyncTokenAsync(email, password, Environment.MachineName, ct);
        _api.AccessToken = token;
        CredentialStore.SaveToken(token);
        CurrentUser = await _api.GetCurrentUserAsync(ct);
    }

    public void Logout()
    {
        _api.AccessToken = null;
        CredentialStore.ClearToken();
        CurrentUser = null;
    }
}
