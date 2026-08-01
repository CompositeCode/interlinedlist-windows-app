using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class ListsViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SessionService _session;

    public ObservableCollection<ListSummary> Lists { get; } = new();
    public ObservableCollection<ListDataRow> Rows { get; } = new();
    public ObservableCollection<WatchedList> SharedWithMe { get; } = new();
    public ObservableCollection<ShareLink> ShareLinks { get; } = new();
    public ObservableCollection<Collaborator> Watchers { get; } = new();
    public ObservableCollection<UserSearchResult> WatcherSearchResults { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string newListTitle = "";

    [ObservableProperty]
    private string newListDescription = "";

    [ObservableProperty]
    private ListSummary? selectedList;

    [ObservableProperty]
    private bool isViewingShared;

    [ObservableProperty]
    private WatchedList? selectedSharedList;

    [ObservableProperty]
    private bool isLoadingRows;

    [ObservableProperty]
    private string newRowJson = "";

    [ObservableProperty]
    private string? rowErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingRow))]
    private ListDataRow? editingRow;

    [ObservableProperty]
    private string editRowJson = "";

    [ObservableProperty]
    private string watcherSearchQuery = "";

    public bool IsEditingRow => EditingRow is not null;

    private static readonly JsonSerializerOptions RowEditJsonOptions = new() { WriteIndented = true };

    public ListsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadListsAsync()
    {
        IsLoading = true;
        try
        {
            var page = await _session.Api.GetListsAsync(limit: PageSize, offset: 0);

            Lists.Clear();
            foreach (var list in page.Lists)
                Lists.Add(list);

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

    private bool CanCreateList() => !string.IsNullOrWhiteSpace(NewListTitle);

    [RelayCommand(CanExecute = nameof(CanCreateList))]
    private async Task CreateListAsync()
    {
        try
        {
            var description = string.IsNullOrWhiteSpace(NewListDescription) ? null : NewListDescription.Trim();
            await _session.Api.CreateListAsync(NewListTitle.Trim(), description);
            NewListTitle = "";
            NewListDescription = "";
            await LoadListsAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteListAsync(ListSummary list)
    {
        try
        {
            await _session.Api.DeleteListAsync(list.Id);
            if (SelectedList == list)
            {
                SelectedList = null;
                Rows.Clear();
                ShareLinks.Clear();
                Watchers.Clear();
                WatcherSearchResults.Clear();
                WatcherSearchQuery = "";
            }
            await LoadListsAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadSharedAsync()
    {
        try
        {
            var shared = await _session.Api.GetWatchingListsAsync();

            SharedWithMe.Clear();
            foreach (var list in shared)
                SharedWithMe.Add(list);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SelectListAsync(ListSummary list)
    {
        IsViewingShared = false;
        SelectedSharedList = null;
        SelectedList = list;
        WatcherSearchQuery = "";
        WatcherSearchResults.Clear();
        await LoadRowsAsync(list.Id);
        await LoadShareLinksAsync(list.Id);
        await LoadWatchersAsync(list.Id);
    }

    [RelayCommand]
    private async Task SelectSharedListAsync(WatchedList watched)
    {
        IsViewingShared = true;
        SelectedList = null;
        SelectedSharedList = watched;
        EditingRow = null;
        EditRowJson = "";
        ShareLinks.Clear();
        Watchers.Clear();
        WatcherSearchResults.Clear();
        WatcherSearchQuery = "";
        await LoadRowsAsync(watched.Id);
    }

    private async Task LoadShareLinksAsync(string listId)
    {
        try
        {
            var links = await _session.Api.GetListShareLinksAsync(listId);

            ShareLinks.Clear();
            foreach (var link in links)
                ShareLinks.Add(link);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private bool CanManageShareLinks() => SelectedList is not null && !IsViewingShared;

    [RelayCommand(CanExecute = nameof(CanManageShareLinks))]
    private async Task CreateShareLinkAsync()
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.CreateListShareLinkAsync(list.Id);
            await LoadShareLinksAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RevokeShareLinkAsync(ShareLink link)
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.DeleteListShareLinkAsync(list.Id, link.Token);
            await LoadShareLinksAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CopyShareLink(ShareLink link)
    {
        if (string.IsNullOrEmpty(link.Url)) return;
        System.Windows.Clipboard.SetText(link.Url);
    }

    // ── Watchers (per-user shared access to an own list) ────────────────────────

    private async Task LoadWatchersAsync(string listId)
    {
        try
        {
            var watchers = await _session.Api.GetListWatchersAsync(listId);

            Watchers.Clear();
            foreach (var watcher in watchers)
                Watchers.Add(watcher);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private bool CanManageWatchers() => SelectedList is not null && !IsViewingShared;

    [RelayCommand(CanExecute = nameof(CanManageWatchers))]
    private async Task SearchWatcherUsersAsync()
    {
        if (SelectedList is not { } list) return;
        if (string.IsNullOrWhiteSpace(WatcherSearchQuery)) return;
        try
        {
            var users = await _session.Api.SearchListWatcherUsersAsync(list.Id, WatcherSearchQuery.Trim());

            WatcherSearchResults.Clear();
            foreach (var user in users)
                WatcherSearchResults.Add(user);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageWatchers))]
    private async Task AddWatcherAsync(UserSearchResult user)
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.AddListWatcherAsync(list.Id, user.Id);
            WatcherSearchQuery = "";
            WatcherSearchResults.Clear();
            await LoadWatchersAsync(list.Id);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageWatchers))]
    private async Task RemoveWatcherAsync(Collaborator watcher)
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.RemoveListWatcherAsync(list.Id, watcher.UserId);
            await LoadWatchersAsync(list.Id);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadRowsAsync(string listId)
    {
        IsLoadingRows = true;
        try
        {
            var page = await _session.Api.GetListDataAsync(listId, limit: PageSize, offset: 0);

            Rows.Clear();
            foreach (var row in page.Rows)
                Rows.Add(row);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingRows = false;
        }
    }

    private bool CanAddRow() => SelectedList is not null && !string.IsNullOrWhiteSpace(NewRowJson);

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private async Task AddRowAsync()
    {
        if (SelectedList is not { } list) return;

        Dictionary<string, object?> parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(NewRowJson)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            RowErrorMessage = "That's not valid JSON.";
            return;
        }

        try
        {
            await _session.Api.AddListRowAsync(list.Id, parsed);
            NewRowJson = "";
            RowErrorMessage = null;
            await LoadRowsAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void StartEditRow(ListDataRow row)
    {
        EditingRow = row;
        EditRowJson = JsonSerializer.Serialize(row.RowData, RowEditJsonOptions);
        RowErrorMessage = null;
    }

    [RelayCommand]
    private void CancelEditRow()
    {
        EditingRow = null;
        EditRowJson = "";
    }

    [RelayCommand]
    private async Task SaveRowEditAsync()
    {
        if (SelectedList is not { } list || EditingRow is not { } row) return;

        Dictionary<string, object?> parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(EditRowJson)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            RowErrorMessage = "That's not valid JSON.";
            return;
        }

        try
        {
            await _session.Api.UpdateListRowAsync(list.Id, row.Id, parsed);
            EditingRow = null;
            EditRowJson = "";
            RowErrorMessage = null;
            await LoadRowsAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteRowAsync(ListDataRow row)
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.DeleteListRowAsync(list.Id, row.Id);
            await LoadRowsAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    partial void OnNewListTitleChanged(string value) => CreateListCommand.NotifyCanExecuteChanged();

    partial void OnSelectedListChanged(ListSummary? value)
    {
        AddRowCommand.NotifyCanExecuteChanged();
        CreateShareLinkCommand.NotifyCanExecuteChanged();
        SearchWatcherUsersCommand.NotifyCanExecuteChanged();
        AddWatcherCommand.NotifyCanExecuteChanged();
        RemoveWatcherCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsViewingSharedChanged(bool value)
    {
        CreateShareLinkCommand.NotifyCanExecuteChanged();
        SearchWatcherUsersCommand.NotifyCanExecuteChanged();
        AddWatcherCommand.NotifyCanExecuteChanged();
        RemoveWatcherCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewRowJsonChanged(string value) => AddRowCommand.NotifyCanExecuteChanged();
}
