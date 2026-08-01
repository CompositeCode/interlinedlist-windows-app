using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<Message> MessageResults { get; } = new();
    public ObservableCollection<UserSearchResult> UserResults { get; } = new();
    public ObservableCollection<ListSearchResult> ListResults { get; } = new();
    public ObservableCollection<DocumentSearchResult> DocumentResults { get; } = new();

    [ObservableProperty]
    private string query = "";

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private string? errorMessage;

    public SearchViewModel(SessionService session)
    {
        _session = session;
    }

    private bool CanSearch() => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var q = Query;

        MessageResults.Clear();
        UserResults.Clear();
        ListResults.Clear();
        DocumentResults.Clear();
        ErrorMessage = null;

        IsSearching = true;
        try
        {
            try
            {
                var messagesPage = await _session.Api.SearchMessagesAsync(q);
                foreach (var message in messagesPage.Messages)
                    MessageResults.Add(message);
            }
            catch (InterlinedApiException ex)
            {
                MessageResults.Clear();
                ErrorMessage = ex.Message;
            }

            try
            {
                var usersPage = await _session.Api.SearchUsersAsync(q);
                foreach (var user in usersPage.Users)
                    UserResults.Add(user);
            }
            catch (InterlinedApiException ex)
            {
                UserResults.Clear();
                ErrorMessage = ex.Message;
            }

            try
            {
                var lists = await _session.Api.SearchListsAsync(q);
                foreach (var list in lists)
                    ListResults.Add(list);
            }
            catch (InterlinedApiException ex)
            {
                ListResults.Clear();
                ErrorMessage = ex.Message;
            }

            try
            {
                var documents = await _session.Api.SearchDocumentsAsync(q);
                foreach (var document in documents)
                    DocumentResults.Add(document);
            }
            catch (InterlinedApiException ex)
            {
                DocumentResults.Clear();
                ErrorMessage = ex.Message;
            }
        }
        finally
        {
            IsSearching = false;
        }
    }

    partial void OnQueryChanged(string value) => SearchCommand.NotifyCanExecuteChanged();
}
