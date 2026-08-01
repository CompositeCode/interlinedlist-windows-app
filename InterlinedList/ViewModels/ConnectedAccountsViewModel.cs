using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class ConnectedAccountsViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<LinkedIdentity> Identities { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string mastodonInstance = "";

    public ConnectedAccountsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var identities = await _session.Api.GetLinkedIdentitiesAsync();

            Identities.Clear();
            foreach (var identity in identities)
                Identities.Add(identity);

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
    private void ConnectBluesky() => _session.Api.OpenProviderAuthorize("bluesky");

    [RelayCommand]
    private void ConnectLinkedIn() => _session.Api.OpenProviderAuthorize("linkedin");

    [RelayCommand]
    private void ConnectTwitter() => _session.Api.OpenProviderAuthorize("twitter");

    private bool CanConnectMastodon() => !string.IsNullOrWhiteSpace(MastodonInstance);

    [RelayCommand(CanExecute = nameof(CanConnectMastodon))]
    private void ConnectMastodon() => _session.Api.OpenProviderAuthorize("mastodon", MastodonInstance.Trim());

    // Unlink's response shape isn't live-verified (see InterlinedApiClient.CrossPost.cs) —
    // treat success as "assume it worked" and just reload the list.
    [RelayCommand]
    private async Task DisconnectAsync(LinkedIdentity identity)
    {
        try
        {
            await _session.Api.RemoveIdentityAsync(identity.Provider);
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnMastodonInstanceChanged(string value) => ConnectMastodonCommand.NotifyCanExecuteChanged();
}
