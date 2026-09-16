using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The column form builder — add / edit / remove / reorder a list's columns
/// across all twelve field types, either for a list that doesn't exist yet
/// (<see cref="IsNewListMode"/>, the schema rides along on
/// <c>POST /api/lists</c>) or for a saved one.
///
/// Saving a SAVED list goes through the non-destructive <c>properties</c> PUT
/// only. That is the whole reason this editor exists in two pieces: the
/// destructive DSL rebuild is a separate, explicitly-labelled action (#22), and
/// until it lands a draft that can only be expressed as a rebuild is refused
/// here with an explanation rather than quietly wiping every column.
///
/// Everything the editor does about the API's sharp edges is live-verified
/// (2026-09-16, throwaway lists since deleted):
/// <list type="bullet">
///   <item>the <c>properties</c> body accepts only six of the twelve types, so
///   picking a richer one is warned about at the moment of the click;</item>
///   <item>an omitted field on a <c>properties</c> item is CLEARED, so every
///   draft always sends its full projection;</item>
///   <item>dropping a column that still holds data answers <c>409</c> with
///   <c>propertiesWithData</c>, which becomes an in-place confirmation naming
///   those columns instead of a bare error;</item>
///   <item>a saved column's key can't change (<c>400</c>), so the key box locks.</item>
/// </list>
/// </summary>
public partial class ListColumnEditorViewModel : ObservableObject
{
    private readonly SessionService _session;

    /// <summary>Raised after a successful save so the host can re-read the list.</summary>
    public event EventHandler? Saved;

    public ObservableCollection<ListColumnDraftViewModel> Columns { get; } = new();

    /// <summary>Keys the list had when the editor opened — used to spell out removals.</summary>
    private readonly List<string> _storedKeys = new();

    private List<ListPropertyUpdate>? _pendingForcedSave;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isNewListMode;

    [ObservableProperty]
    private ListSummary? list;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    /// <summary>Set when a save was refused with 409; names the data-bearing columns.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsForceConfirmation))]
    private string? forceConfirmationMessage;

    public bool NeedsForceConfirmation => ForceConfirmationMessage is not null;

    public string HeaderText => IsNewListMode
        ? "Columns for the new list"
        : List is { } list ? $"Columns — {list.Title}" : "Columns";

    public string SaveButtonText => IsNewListMode ? "Use these columns" : "Save columns";

    /// <summary>
    /// True while every column in the draft is one of the six the safe path
    /// accepts. False means the only way to save is the destructive rebuild.
    /// </summary>
    public bool CanUsePropertiesPath => Columns.Count > 0 && Columns.All(c => c.SupportsPropertiesEdit);

    /// <summary>Columns that were saved on the list but are no longer in the draft.</summary>
    public IReadOnlyList<string> RemovedKeys =>
        _storedKeys.Where(k => !Columns.Any(c => string.Equals(c.StoredKey, k, StringComparison.Ordinal)))
                   .ToList();

    public string? RemovalWarning => RemovedKeys.Count == 0
        ? null
        : $"Saving deletes {string.Join(", ", RemovedKeys)} and strips those keys from every row.";

    public string? RebuildOnlyWarning => IsNewListMode || CanUsePropertiesPath
        ? null
        : "This draft uses "
          + string.Join(", ", Columns.Where(c => !c.SupportsPropertiesEdit)
                                     .Select(c => ListFieldType.DisplayName(c.Type))
                                     .Distinct(StringComparer.Ordinal))
          + " — types the safe single-column save can't express (it only accepts "
          + string.Join(", ", ListFieldType.PropertiesEditable)
          + "). Saving those requires the destructive column rebuild.";

    // ── The two write paths, deliberately two actions ───────────────────────
    // PUT /api/lists/{id}/schema is one route with two bodies whose consequences
    // are nothing alike, so the UI never chooses for the user:
    //
    //   "Save columns"     → { properties: [ … ] }  updates columns in place,
    //                        row data untouched, column ids kept.
    //   "Rebuild columns…" → { schema: { … } }      drops and recreates EVERY
    //                        column, overwrites the list's title AND description,
    //                        and leaves values whose key no longer has a column
    //                        orphaned in rowData.
    //
    // Both measured live 2026-09-16. Note the asymmetry in what "removing a
    // column" costs, which is why the two confirmations read differently:
    //   * properties + ?force=true STRIPS the key from every row (data deleted);
    //   * a rebuild LEAVES the value in rowData, unshown and unvalidated.

    /// <summary>True while the destructive rebuild is waiting for confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RebuildImpactSummary))]
    private bool isRebuildConfirmationOpen;

    /// <summary>
    /// The title the rebuild will write. Editable because the rebuild writes it
    /// whether the user meant to or not: <c>schema.name</c> overwrote a list's
    /// title in testing, so the box is pre-filled with the current one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RebuildImpactSummary))]
    private string rebuildTitle = "";

    /// <summary>
    /// The description the rebuild will write. Blank CLEARS it — omitting
    /// <c>schema.description</c> wiped a list's description in testing — so the
    /// box is pre-filled and the confirmation says so.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RebuildImpactSummary))]
    private string rebuildDescription = "";

    /// <summary>What the SAFE save is about to do, in the user's terms.</summary>
    public string SavePlanSummary
    {
        get
        {
            if (IsNewListMode)
                return $"{Columns.Count} column(s) will be created together with the list.";

            var parts = new List<string>();

            if (Columns.Count(c => !c.IsExistingColumn) is > 0 and var added)
                parts.Add($"adds {added}");
            if (Columns.Count(c => c.IsRenamed) is > 0 and var renamed)
                parts.Add($"renames {renamed}");
            if (Columns.Count(c => c.IsRetyped) is > 0 and var retyped)
                parts.Add($"retypes {retyped}");
            if (RemovedKeys.Count > 0)
                parts.Add($"deletes {string.Join(", ", RemovedKeys)}");
            if (IsReordered)
                parts.Add("reorders the columns");

            return parts.Count == 0
                ? "No column changes yet."
                : $"Save columns {string.Join(", ", parts)} — in place, with every row's data kept.";
        }
    }

    /// <summary>True when the saved columns are no longer in their saved order.</summary>
    private bool IsReordered
    {
        get
        {
            var orders = Columns.Where(c => c.IsExistingColumn)
                                .Select(c => c.StoredOrder ?? 0)
                                .ToList();
            return orders.Zip(orders.Skip(1)).Any(pair => pair.Second < pair.First);
        }
    }

    /// <summary>
    /// Exactly what the rebuild costs, with the affected columns named. Says
    /// nothing about rows being deleted, because they are not: the measured
    /// cost is orphaned values, new column ids, and an overwritten
    /// title/description.
    /// </summary>
    public string RebuildImpactSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"All {Columns.Count} column(s) are dropped and recreated with new ids."
            };

            lines.Add(RemovedKeys.Count > 0
                ? "Your rows are NOT deleted — but the values under "
                  + string.Join(", ", RemovedKeys)
                  + " stay in each row with no column to show or validate them."
                : "Your rows are NOT deleted, and every column here keeps its key, so their values stay reachable.");

            var carried = Columns.Count(c => c.Validation is { IsEmpty: false } || c.Visibility?.Condition is not null);
            lines.Add(carried > 0
                ? $"Validation rules and visibility conditions on {carried} column(s) are re-sent as they were read, so they survive — anything added on the web since this editor opened does not."
                : "Any validation rule or visibility condition a column has that this editor didn't read is lost.");

            lines.Add(string.IsNullOrWhiteSpace(RebuildTitle)
                ? "The list needs a title — the rebuild writes it from this box."
                : $"The list's title becomes “{RebuildTitle.Trim()}”.");

            lines.Add(string.IsNullOrWhiteSpace(RebuildDescription)
                ? "The list's description is CLEARED (a blank box clears it)."
                : $"The list's description becomes “{RebuildDescription.Trim()}”.");

            return string.Join("\n", lines.Select(line => "• " + line));
        }
    }

    private bool CanStartRebuild() => !IsBusy && !IsNewListMode && List is not null && IsDraftSavable;

    /// <summary>
    /// Open the rebuild confirmation. Separate from <see cref="SaveCommand"/> on
    /// purpose — a single "Save schema" button that always sent the DSL is the
    /// data-loss footgun this whole split exists to prevent.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartRebuild))]
    private void StartRebuild()
    {
        if (List is not { } list) return;
        if (!ValidateDraft()) return;

        RebuildTitle = list.Title;
        RebuildDescription = list.Description ?? "";
        ForceConfirmationMessage = null;
        _pendingForcedSave = null;
        ErrorMessage = null;
        StatusMessage = null;
        IsRebuildConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelRebuild()
    {
        IsRebuildConfirmationOpen = false;
        StatusMessage = "Nothing was changed.";
    }

    [RelayCommand]
    private async Task ConfirmRebuildAsync()
    {
        if (List is not { } list) return;
        if (!ValidateDraft()) return;

        if (string.IsNullOrWhiteSpace(RebuildTitle))
        {
            ErrorMessage = "The rebuild writes the list's title — give it one.";
            return;
        }

        IsBusy = true;
        SaveCommand.NotifyCanExecuteChanged();
        StartRebuildCommand.NotifyCanExecuteChanged();
        try
        {
            var description = string.IsNullOrWhiteSpace(RebuildDescription) ? null : RebuildDescription.Trim();
            var updated = await _session.Api.RebuildListSchemaDestructiveAsync(
                list.Id, BuildSchema(RebuildTitle.Trim(), description));

            IsRebuildConfirmationOpen = false;
            ErrorMessage = null;
            List = updated;
            StatusMessage = $"Rebuilt {Columns.Count} column(s). The list is now titled “{updated.Title}”. "
                          + "Rows were kept; any value whose column is gone is still stored but no longer shown.";
            await ReloadAsync();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ListSchemaException ex)
        {
            AttachIssues(ex);
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
            StartRebuildCommand.NotifyCanExecuteChanged();
        }
    }

    public ListColumnEditorViewModel(SessionService session)
    {
        _session = session;
        Columns.CollectionChanged += OnColumnsChanged;
    }

    // The "can this be saved safely" answer depends on each column's TYPE, not
    // just on how many columns there are, so the editor listens to the drafts too.
    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<ListColumnDraftViewModel>() ?? [])
            removed.PropertyChanged -= OnDraftPropertyChanged;
        foreach (var added in e.NewItems?.OfType<ListColumnDraftViewModel>() ?? [])
            added.PropertyChanged += OnDraftPropertyChanged;

        NotifyDraftSetChanged();
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IssueMessage is set BY this class; reacting to it would recurse.
        if (e.PropertyName == nameof(ListColumnDraftViewModel.IssueMessage)) return;
        NotifyDraftSetChanged();
    }

    /// <summary>Clear with the per-draft subscriptions detached (a Reset event carries no OldItems).</summary>
    private void ClearColumns()
    {
        foreach (var column in Columns)
            column.PropertyChanged -= OnDraftPropertyChanged;
        Columns.Clear();
    }

    /// <summary>Open the builder for a list that hasn't been created yet.</summary>
    [RelayCommand]
    private void OpenForNewList()
    {
        Reset();
        IsNewListMode = true;
        List = null;
        Columns.Add(ListColumnDraftViewModel.ForNewColumn());
        IsOpen = true;
    }

    /// <summary>Open the builder on a saved list, reading its current columns.</summary>
    public async Task OpenForListAsync(ListSummary list)
    {
        Reset();
        IsNewListMode = false;
        List = list;
        IsOpen = true;
        await ReloadAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Reset();
    }

    private void Reset()
    {
        ClearColumns();
        _storedKeys.Clear();
        _pendingForcedSave = null;
        ForceConfirmationMessage = null;
        IsRebuildConfirmationOpen = false;
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>
    /// Read the columns twice, on purpose: the DSL view carries label/default/
    /// options/validation/visibility, while <c>GET /api/lists/{id}</c> is the only
    /// place each column's id lives — and the non-destructive PUT needs that id
    /// to update a column in place instead of recreating it.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (List is not { } list) return;

        IsBusy = true;
        try
        {
            var schema = await _session.Api.GetListSchemaAsync(list.Id);
            var stored = await _session.Api.GetListPropertiesAsync(list.Id);

            ClearColumns();
            _storedKeys.Clear();
            foreach (var field in schema.FieldsInDisplayOrder)
            {
                var match = stored.FirstOrDefault(p => string.Equals(p.PropertyKey, field.Key, StringComparison.Ordinal));
                Columns.Add(ListColumnDraftViewModel.FromField(field, match));
                _storedKeys.Add(field.Key);
            }

            StatusMessage = Columns.Count == 0
                ? "This list has no columns yet — its rows are freeform JSON. Add one to give it a shape."
                : null;
            ErrorMessage = null;
        }
        catch (ListSchemaException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyDraftSetChanged();
        }
    }

    [RelayCommand]
    private void AddColumn()
    {
        Columns.Add(ListColumnDraftViewModel.ForNewColumn());
        StatusMessage = null;
    }

    [RelayCommand]
    private void RemoveColumn(ListColumnDraftViewModel column)
    {
        Columns.Remove(column);
        ForceConfirmationMessage = null;
        _pendingForcedSave = null;
    }

    [RelayCommand]
    private void MoveColumnUp(ListColumnDraftViewModel column)
    {
        var index = Columns.IndexOf(column);
        if (index > 0) Columns.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveColumnDown(ListColumnDraftViewModel column)
    {
        var index = Columns.IndexOf(column);
        if (index >= 0 && index < Columns.Count - 1) Columns.Move(index, index + 1);
    }

    /// <summary>
    /// The DSL the draft describes. <paramref name="name"/> is the LIST's title
    /// on both write paths (the rebuild PUT overwrites it), so the caller passes
    /// the title it means to end up with.
    /// </summary>
    public ListSchema BuildSchema(string name, string? description) => new()
    {
        Name = name,
        Description = description,
        Fields = Columns.Select((c, i) => c.ToField(i)).ToList()
    };

    /// <summary>
    /// Why the draft can't be saved yet, recomputed on every keystroke so the
    /// save button is disabled (not merely refused) while a select column has no
    /// options or a column has no key — #18's "before save is enabled" rule.
    /// </summary>
    public bool IsDraftSavable => DraftBlockers().Count == 0;

    /// <summary>The first blocker, shown beside the disabled save button.</summary>
    public string? SaveBlockedReason => DraftBlockers().FirstOrDefault();

    private List<string> DraftBlockers()
    {
        var blockers = new List<string>();

        if (Columns.Count == 0)
        {
            blockers.Add("Add at least one column — a schema with none is rejected.");
            return blockers;
        }

        foreach (var column in Columns)
            blockers.AddRange(column.LocalIssues().Select(issue => $"{Describe(column)}: {issue}"));

        foreach (var group in Columns
                     .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                     .GroupBy(c => c.Key.Trim(), StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            blockers.Add($"Two columns share the key '{group.Key}'.");
        }

        return blockers;
    }

    private static string Describe(ListColumnDraftViewModel column) =>
        !string.IsNullOrWhiteSpace(column.Label) ? column.Label.Trim()
        : !string.IsNullOrWhiteSpace(column.Key) ? column.Key.Trim()
        : "New column";

    /// <summary>
    /// Client-side gate, so a bad draft never becomes a request: at least one
    /// column, no duplicate keys, every column individually legal, and the same
    /// checks the server runs on a DSL body. Issues are attached to the column
    /// they belong to.
    /// </summary>
    public bool ValidateDraft()
    {
        foreach (var column in Columns)
            column.IssueMessage = null;

        var blocking = new List<string>();

        if (Columns.Count == 0)
        {
            ErrorMessage = "A schema needs at least one column (the server rejects an empty one).";
            return false;
        }

        foreach (var column in Columns)
        {
            if (column.LocalIssues() is { Count: > 0 } issues)
            {
                column.IssueMessage = string.Join(" ", issues);
                blocking.AddRange(issues);
            }
        }

        foreach (var group in Columns
                     .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                     .GroupBy(c => c.Key.Trim(), StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            foreach (var column in group)
                column.IssueMessage = $"Duplicate key '{group.Key}'.";
            blocking.Add($"Duplicate key '{group.Key}'.");
        }

        if (blocking.Count > 0)
        {
            ErrorMessage = "Fix the columns marked below.";
            return false;
        }

        // The server's own DSL validator, mirrored client-side — catches the
        // conditional-visibility mistakes it would otherwise accept and ignore.
        var schemaIssues = BuildSchema(List?.Title ?? "Untitled", List?.Description).Validate();
        if (schemaIssues.Count > 0)
        {
            foreach (var issue in schemaIssues)
            {
                var column = Columns.FirstOrDefault(c => string.Equals(c.Key.Trim(), issue.FieldKey, StringComparison.Ordinal));
                if (column is not null) column.IssueMessage = issue.ShortMessage;
            }
            ErrorMessage = schemaIssues[0].ShortMessage;
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    private bool CanSave() => !IsBusy && IsDraftSavable;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!ValidateDraft()) return;

        // New-list mode never writes: the host posts the schema with the list.
        if (IsNewListMode)
        {
            StatusMessage = $"{Columns.Count} column(s) ready — they're created with the list.";
            IsOpen = false;
            Saved?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!CanUsePropertiesPath)
        {
            ErrorMessage = RebuildOnlyWarning + " Use “Rebuild columns” for that — it says what the rebuild costs before it runs.";
            return;
        }

        await SendPropertiesAsync(Columns.Select((c, i) => c.ToPropertyUpdate(i)).ToList(), force: false);
    }

    /// <summary>Re-send the same edit with <c>?force=true</c> after the 409 was confirmed.</summary>
    [RelayCommand]
    private async Task ConfirmForcedSaveAsync()
    {
        if (_pendingForcedSave is not { } items) return;
        ForceConfirmationMessage = null;
        await SendPropertiesAsync(items, force: true);
    }

    [RelayCommand]
    private void CancelForcedSave()
    {
        ForceConfirmationMessage = null;
        _pendingForcedSave = null;
        StatusMessage = "Nothing was changed.";
    }

    private async Task SendPropertiesAsync(List<ListPropertyUpdate> items, bool force)
    {
        if (List is not { } list) return;

        IsBusy = true;
        SaveCommand.NotifyCanExecuteChanged();
        try
        {
            var saved = await _session.Api.UpdateListPropertiesAsync(list.Id, items, force);
            _pendingForcedSave = null;
            ErrorMessage = null;
            StatusMessage = $"Saved {saved.Count} column(s). Row data was preserved.";
            await ReloadAsync();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ListSchemaException ex) when (ex.RequiresForce)
        {
            // Name the columns rather than showing a bare 409.
            _pendingForcedSave = items;
            var affected = ex.PropertiesWithData
                .Select(k => Columns.FirstOrDefault(c => string.Equals(c.StoredKey, k, StringComparison.Ordinal))?.Label is { Length: > 0 } label
                    ? $"{label} ({k})" : k);
            ForceConfirmationMessage =
                $"{string.Join(", ", affected)} still hold row data. Deleting them removes that value "
                + "from every row in this list. Everything else about the rows is kept.";
            StatusMessage = null;
            ErrorMessage = null;
        }
        catch (ListSchemaException ex)
        {
            AttachIssues(ex);
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

    private void AttachIssues(ListSchemaException ex)
    {
        foreach (var issue in ex.Issues)
        {
            var column = Columns.FirstOrDefault(c =>
                string.Equals(c.StoredKey, issue.FieldKey, StringComparison.Ordinal)
                || string.Equals(c.Key.Trim(), issue.FieldKey, StringComparison.Ordinal));
            if (column is null && issue.FieldIndex is { } index && index >= 0 && index < Columns.Count)
                column = Columns[index];
            if (column is not null) column.IssueMessage = issue.ShortMessage;
        }
        ErrorMessage = ex.Message;
    }

    private void NotifyDraftSetChanged()
    {
        OnPropertyChanged(nameof(CanUsePropertiesPath));
        OnPropertyChanged(nameof(RemovedKeys));
        OnPropertyChanged(nameof(RemovalWarning));
        OnPropertyChanged(nameof(RebuildOnlyWarning));
        OnPropertyChanged(nameof(IsDraftSavable));
        OnPropertyChanged(nameof(SaveBlockedReason));
        OnPropertyChanged(nameof(SavePlanSummary));
        OnPropertyChanged(nameof(RebuildImpactSummary));
        SaveCommand.NotifyCanExecuteChanged();
        StartRebuildCommand.NotifyCanExecuteChanged();
    }
}
