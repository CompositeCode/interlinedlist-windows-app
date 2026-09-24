using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The typed row form — one control per column, generated from the list's live
/// schema, replacing the raw-JSON box for schema'd lists (the JSON escape hatch
/// stays for schema-less ones, which the API still fully supports).
///
/// Three things it has to get right, all live-verified 2026-09-16:
/// <list type="bullet">
///   <item>Row data is keyed by each column's <b>key</b>, never its label.</item>
///   <item>A row write REPLACES <c>rowData</c>, so the form re-sends every key
///   it knows about — including keys with no column at all, which are kept
///   verbatim (and are exactly what a destructive rebuild leaves behind).</item>
///   <item>A refused write is <c>422</c> with <c>details:[{field,message}]</c>,
///   which <see cref="ListRowValidationException"/> unpacks so each message
///   lands on the control that caused it.</item>
/// </list>
/// </summary>
public partial class ListRowEditorViewModel : ObservableObject
{
    private readonly SessionService _session;

    /// <summary>Raised after a successful write so the host can re-read the rows.</summary>
    public event EventHandler? Saved;

    public ObservableCollection<ListRowFieldViewModel> Fields { get; } = new();

    /// <summary>Stored keys with no column — carried through a save untouched.</summary>
    private Dictionary<string, JsonElement> _keysWithoutColumns = new(StringComparer.Ordinal);

    private ListDataRow? _row;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isEditing;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    /// <summary>Columns saved with <c>visible: false</c> are folded away until asked for.</summary>
    [ObservableProperty]
    private bool showHiddenColumns;

    /// <summary>Set while editing a row that holds values no column covers.</summary>
    [ObservableProperty]
    private string? orphanNote;

    public string? ListId { get; private set; }

    public string HeaderText => IsEditing ? "Edit row" : "New row";

    public string SaveButtonText => IsEditing ? "Save row" : "Add row";

    public bool HasHiddenColumns => Fields.Any(f => f.IsHiddenColumn);

    public ListRowEditorViewModel(SessionService session)
    {
        _session = session;
    }

    /// <summary>Open a blank form, pre-filled from each column's <c>defaultValue</c>.</summary>
    public void StartAdd(string listId, ListSchema schema)
    {
        Build(listId, schema);
        _row = null;
        IsEditing = false;

        foreach (var field in Fields)
            field.LoadDefault();

        RefreshVisibility();
        IsOpen = true;
    }

    /// <summary>Open the form on an existing row.</summary>
    public void StartEdit(string listId, ListSchema schema, ListDataRow row)
    {
        Build(listId, schema);
        _row = row;
        IsEditing = true;

        foreach (var field in Fields)
            field.LoadStored(row.RowData.TryGetValue(field.Key, out var value) ? value : null);

        _keysWithoutColumns = row.RowData
            .Where(kv => schema.FindField(kv.Key) is null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        OrphanNote = _keysWithoutColumns.Count == 0
            ? null
            : $"This row also holds {string.Join(", ", _keysWithoutColumns.Keys)} — no column covers "
              + "those any more, so they aren't shown. Saving keeps them as they are.";

        RefreshVisibility();
        IsOpen = true;
    }

    private void Build(string listId, ListSchema schema)
    {
        foreach (var field in Fields)
            field.ValueChanged -= OnFieldValueChanged;
        Fields.Clear();

        ListId = listId;
        ErrorMessage = null;
        StatusMessage = null;
        OrphanNote = null;
        _keysWithoutColumns = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var field in schema.FieldsInDisplayOrder)
        {
            var vm = new ListRowFieldViewModel(field) { ShowHiddenColumns = ShowHiddenColumns };
            vm.ValueChanged += OnFieldValueChanged;
            Fields.Add(vm);
        }

        OnPropertyChanged(nameof(HasHiddenColumns));
    }

    private void OnFieldValueChanged(object? sender, EventArgs e) => RefreshVisibility();

    /// <summary>
    /// Re-run every conditional-visibility rule against the values held right
    /// now. The server never evaluates these, so without this pass a conditional
    /// column would simply always show.
    /// </summary>
    private void RefreshVisibility()
    {
        foreach (var field in Fields)
            field.MatchesCondition = ListFieldConditionEvaluator.IsShown(field.Field, ValueOf);
    }

    private object? ValueOf(string key)
    {
        var field = Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
        if (field is not null) return field.CurrentValue();
        return _keysWithoutColumns.TryGetValue(key, out var stored) ? stored : null;
    }

    /// <summary>Close the form without writing (also used by the host when the selection changes).</summary>
    [RelayCommand]
    public void Cancel()
    {
        IsOpen = false;
        ErrorMessage = null;
        StatusMessage = null;
    }

    private bool CanSave() => !IsBusy && ListId is { Length: > 0 };

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (ListId is not { Length: > 0 } listId) return;

        // Start from the keys no column covers so a save never drops them (the
        // write replaces rowData wholesale).
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in _keysWithoutColumns)
            data[key] = value;

        var blocked = false;
        foreach (var field in Fields)
        {
            if (field.TryBuildValue(out var value, out var error))
            {
                field.ErrorMessage = null;

                // Don't invent keys: a blank optional column the row never had
                // stays out of rowData. Clearing a stored value still sends null.
                if (value is not null || field.HasStoredValue)
                    data[field.Key] = value;
            }
            else
            {
                field.ErrorMessage = error;
                blocked = true;
            }
        }

        if (blocked)
        {
            ErrorMessage = "Fix the fields marked below.";
            return;
        }

        IsBusy = true;
        SaveCommand.NotifyCanExecuteChanged();
        try
        {
            if (IsEditing && _row is { } row)
                await _session.Api.UpdateListRowAsync(listId, row.Id, data);
            else
                await _session.Api.AddListRowAsync(listId, data);

            ErrorMessage = null;
            StatusMessage = null;
            IsOpen = false;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ListRowValidationException ex)
        {
            AttachDetails(ex);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Put each 422 detail on the control whose key it names.</summary>
    private void AttachDetails(ListRowValidationException ex)
    {
        var unattributed = new List<string>();

        foreach (var detail in ex.Details)
        {
            var field = Fields.FirstOrDefault(f => string.Equals(f.Key, detail.Field, StringComparison.Ordinal));
            if (field is not null)
            {
                field.ErrorMessage = detail.Message;
                // A rule can fire on a column the user can't see; show it rather
                // than leaving a message nobody can reach.
                if (!field.IsShown) unattributed.Add($"{field.Label}: {detail.Message}");
            }
            else
            {
                unattributed.Add($"{detail.Field}: {detail.Message}");
            }
        }

        ErrorMessage = unattributed.Count > 0
            ? string.Join(" ", unattributed)
            : ex.Details.Count > 0 ? "Fix the fields marked below." : ex.Message;
    }

    partial void OnShowHiddenColumnsChanged(bool value)
    {
        foreach (var field in Fields)
            field.ShowHiddenColumns = value;
    }
}
