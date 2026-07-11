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
    private bool isLoadingRows;

    [ObservableProperty]
    private string newRowJson = "";

    [ObservableProperty]
    private string? rowErrorMessage;

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
            }
            await LoadListsAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SelectListAsync(ListSummary list)
    {
        SelectedList = list;
        await LoadRowsAsync(list);
    }

    private async Task LoadRowsAsync(ListSummary list)
    {
        IsLoadingRows = true;
        try
        {
            var page = await _session.Api.GetListDataAsync(list.Id, limit: PageSize, offset: 0);

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
            await LoadRowsAsync(list);
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    partial void OnNewListTitleChanged(string value) => CreateListCommand.NotifyCanExecuteChanged();

    partial void OnSelectedListChanged(ListSummary? value) => AddRowCommand.NotifyCanExecuteChanged();

    partial void OnNewRowJsonChanged(string value) => AddRowCommand.NotifyCanExecuteChanged();
}
