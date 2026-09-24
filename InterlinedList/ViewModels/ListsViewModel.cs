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
    public ObservableCollection<ListRowViewModel> Rows { get; } = new();
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
    private ListRowViewModel? editingRow;

    [ObservableProperty]
    private string editRowJson = "";

    [ObservableProperty]
    private string watcherSearchQuery = "";

    /// <summary>The selected list's columns, or null while it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSchema))]
    [NotifyPropertyChangedFor(nameof(ShowTypedRowEditor))]
    [NotifyPropertyChangedFor(nameof(ShowRawJsonRowEditor))]
    [NotifyPropertyChangedFor(nameof(RowEditorModeNote))]
    private ListSchema? selectedSchema;

    /// <summary>
    /// The raw-JSON escape hatch. It is the ONLY editor for a schema-less list
    /// (still fully supported: such a list answers GET …/schema with
    /// <c>fields: []</c> and accepts arbitrary, even nested, keys — verified live
    /// 2026-09-16) and stays available on a schema'd one for orphaned keys and
    /// anything the typed form can't express.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTypedRowEditor))]
    [NotifyPropertyChangedFor(nameof(ShowRawJsonRowEditor))]
    [NotifyPropertyChangedFor(nameof(RowEditorModeNote))]
    private bool useRawJsonRowEditor;

    public bool IsEditingRow => EditingRow is not null;

    public bool HasSchema => SelectedSchema is { HasFields: true };

    public bool ShowTypedRowEditor => HasSchema && !UseRawJsonRowEditor;

    public bool ShowRawJsonRowEditor => !ShowTypedRowEditor;

    public string RowEditorModeNote => HasSchema
        ? "This list has columns, so rows are edited through typed fields."
        : "This list has no columns — rows are freeform JSON. Add columns to get a typed form.";

    private static readonly JsonSerializerOptions RowEditJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The column form builder (#18). Hosted here rather than owned by the view
    /// so the create-list form can hand its draft straight to
    /// <c>POST /api/lists</c> as a <c>schema</c>, which is the only way to give a
    /// brand-new list columns without an immediately-destructive second call.
    /// </summary>
    public ListColumnEditorViewModel ColumnEditor { get; }

    /// <summary>The typed row form (#21), generated from <see cref="SelectedSchema"/>.</summary>
    public ListRowEditorViewModel RowEditor { get; }

    public ListsViewModel(SessionService session)
    {
        _session = session;
        ColumnEditor = new ListColumnEditorViewModel(session);
        ColumnEditor.Saved += OnColumnsSaved;
        RowEditor = new ListRowEditorViewModel(session);
        RowEditor.Saved += OnRowSaved;
    }

    /// <summary>
    /// A column save can rename the list (only on the destructive rebuild, but
    /// the editor owns that decision) and always changes the shape of the row
    /// form, so re-read the browser and the schema afterwards.
    /// </summary>
    private void OnColumnsSaved(object? sender, EventArgs e)
    {
        if (ColumnEditor.IsNewListMode) return;
        _ = ReloadAfterColumnsSavedAsync();
    }

    private async Task ReloadAfterColumnsSavedAsync()
    {
        await LoadListsAsync();
        if (SelectedList is { } list && !IsViewingShared)
        {
            await LoadSchemaAsync(list.Id);
            await LoadRowsAsync(list.Id);
        }
    }

    private void OnRowSaved(object? sender, EventArgs e)
    {
        var listId = IsViewingShared ? SelectedSharedList?.Id : SelectedList?.Id;
        if (listId is { Length: > 0 } id) _ = LoadRowsAsync(id);
    }

    [RelayCommand]
    private async Task OpenColumnEditorAsync(ListSummary list)
    {
        await ColumnEditor.OpenForListAsync(list);
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
        var title = NewListTitle.Trim();
        var description = string.IsNullOrWhiteSpace(NewListDescription) ? null : NewListDescription.Trim();

        // A draft in the column builder rides along as POST /api/lists' `schema`
        // (live-verified: the same DSL validation as the rebuild PUT, and the
        // list comes back with its columns already created).
        ListSchema? schema = null;
        if (ColumnEditor is { IsNewListMode: true, Columns.Count: > 0 })
        {
            if (!ColumnEditor.ValidateDraft())
            {
                ErrorMessage = "Fix the new list's columns before creating it.";
                return;
            }
            schema = ColumnEditor.BuildSchema(title, description);
        }

        try
        {
            await _session.Api.CreateListAsync(title, description, schema);
            NewListTitle = "";
            NewListDescription = "";
            if (schema is not null) ColumnEditor.CloseCommand.Execute(null);
            await LoadListsAsync();
        }
        catch (ListSchemaException ex)
        {
            ErrorMessage = ex.Message;
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
        RowEditor.Cancel();
        EditingRow = null;
        EditRowJson = "";
        await LoadSchemaAsync(list.Id);
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
        RowEditor.Cancel();
        await LoadSchemaAsync(watched.Id);
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

    /// <summary>
    /// Read the list's columns, which is what the typed row form is generated
    /// from. A list with none answers <c>fields: []</c> — a normal state, not an
    /// error — and the view falls back to the raw-JSON editor.
    /// </summary>
    private async Task LoadSchemaAsync(string listId)
    {
        try
        {
            var schema = await _session.Api.GetListSchemaAsync(listId);
            SelectedSchema = schema.HasFields ? schema : null;
        }
        catch (ListSchemaException)
        {
            SelectedSchema = null;
        }
        catch (InterlinedApiException)
        {
            // A shared list may not expose its schema to a watcher; freeform is
            // the honest fallback rather than an error banner over the rows.
            SelectedSchema = null;
        }
    }

    private async Task LoadRowsAsync(string listId)
    {
        IsLoadingRows = true;
        try
        {
            // GetListRowsAsync, not GetListDataAsync: the endpoint omits each
            // row's listId, which the strict model still requires (#144/PR #146).
            var rows = await _session.Api.GetListRowsAsync(listId, limit: PageSize, offset: 0);

            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(ListRowViewModel.Create(row, SelectedSchema));

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

    /// <summary>Raw-JSON add — the schema-less path, and the escape hatch.</summary>
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
        catch (ListRowValidationException ex)
        {
            RowErrorMessage = ex.Message;
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    private bool CanOpenTypedRowForm() => SelectedList is not null && HasSchema;

    /// <summary>Open the typed form for a new row (defaults pre-filled).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTypedRowForm))]
    private void StartAddRow()
    {
        if (SelectedList is not { } list || SelectedSchema is not { } schema) return;
        EditingRow = null;
        EditRowJson = "";
        RowErrorMessage = null;
        RowEditor.StartAdd(list.Id, schema);
    }

    /// <summary>
    /// Edit a row: through the typed form when the list has columns, through the
    /// JSON box otherwise (or when the hatch is switched on).
    /// </summary>
    [RelayCommand]
    private void StartEditRow(ListRowViewModel row)
    {
        RowErrorMessage = null;

        if (ShowTypedRowEditor && SelectedList is { } list && SelectedSchema is { } schema)
        {
            EditingRow = null;
            EditRowJson = "";
            RowEditor.StartEdit(list.Id, schema, row.Row);
            return;
        }

        RowEditor.Cancel();
        EditingRow = row;
        EditRowJson = JsonSerializer.Serialize(row.Row.RowData, RowEditJsonOptions);
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
        catch (ListRowValidationException ex)
        {
            RowErrorMessage = ex.Message;
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteRowAsync(ListRowViewModel row)
    {
        if (SelectedList is not { } list) return;
        try
        {
            await _session.Api.DeleteListRowAsync(list.Id, row.Id);
            if (EditingRow == row)
            {
                EditingRow = null;
                EditRowJson = "";
            }
            await LoadRowsAsync(list.Id);
        }
        catch (InterlinedApiException ex)
        {
            RowErrorMessage = ex.Message;
        }
    }

    partial void OnNewListTitleChanged(string value) => CreateListCommand.NotifyCanExecuteChanged();

    partial void OnSelectedSchemaChanged(ListSchema? value) => StartAddRowCommand.NotifyCanExecuteChanged();

    partial void OnSelectedListChanged(ListSummary? value)
    {
        AddRowCommand.NotifyCanExecuteChanged();
        StartAddRowCommand.NotifyCanExecuteChanged();
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
