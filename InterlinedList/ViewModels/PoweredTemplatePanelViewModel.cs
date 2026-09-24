using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Powered Templates: describe a list in plain language, get a proposed schema
/// plus starter rows back, edit it, then confirm it into a real list.
///
/// <b>Two calls, two quota units.</b> <c>/suggest</c> previews without writing;
/// <c>/generate</c> persists. Both count against the 50-a-day allowance, so
/// every check that can be made locally is made before either leaves — the
/// 300-word input cap for <c>powered_template</c>, and on confirm: a non-empty
/// title, at least one column, the documented 20-field ceiling, and unique
/// legal field keys. Each of those caught locally is a unit not spent learning
/// it from a 422.
///
/// <b>Read-after-write on confirm.</b> <c>/generate</c>'s envelope is
/// contract-transcribed, not live-verified (it writes to a shared account), so
/// its <c>{listId}</c> is treated as a hint: the panel re-fetches the list with
/// <c>GET /api/lists/{id}</c> and hands the host that, per the repo's
/// read-after-write rule. If the id isn't there, it still refreshes the host's
/// list browser and says so rather than claiming a success it can't prove.
/// </summary>
public sealed partial class PoweredTemplatePanelViewModel : AiPanelViewModelBase
{
    private AiListDsl? _dsl;
    private string? _dslName;
    private string? _dslDescription;

    public PoweredTemplatePanelViewModel(SessionService session, AiAvailabilityService? availability = null)
        : base(session, availability)
    {
    }

    /// <summary>Set by the hosting control from its <c>OpenListCommand</c> dependency property.</summary>
    public ICommand? OpenListCommand { get; set; }

    /// <summary>Set by the hosting control from its <c>RefreshCommand</c> dependency property.</summary>
    public ICommand? RefreshCommand { get; set; }

    // ── Input ───────────────────────────────────────────────────────────────────

    /// <summary>Whether the panel is expanded. Collapsed by default — it's an option, not the default path.</summary>
    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordCount))]
    [NotifyPropertyChangedFor(nameof(WordCountLabel))]
    [NotifyPropertyChangedFor(nameof(IsOverWordCap))]
    private string description = "";

    public int WordCount => AiFeatureLimits.CountWords(Description);

    public int MaxWords => AiFeatureLimits.For(AiFeature.PoweredTemplate).MaxInputWords;

    public string WordCountLabel => $"{WordCount} / {MaxWords} words";

    /// <summary>
    /// Shown before the button is pressed. The cap is mirrored from the server
    /// precisely so an over-long description never costs a unit to reject.
    /// </summary>
    public bool IsOverWordCap => WordCount > MaxWords;

    partial void OnDescriptionChanged(string value) => SuggestCommand.NotifyCanExecuteChanged();

    // ── Preview ─────────────────────────────────────────────────────────────────

    /// <summary>True once a suggestion has come back and is waiting to be confirmed or discarded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColumnCountLabel))]
    private bool hasPreview;

    /// <summary>The proposed list title, editable before confirming.</summary>
    [ObservableProperty]
    private string listTitle = "";

    /// <summary>The proposed list description, editable before confirming.</summary>
    [ObservableProperty]
    private string listDescription = "";

    public ObservableCollection<AiListColumnViewModel> Columns { get; } = new();

    public ObservableCollection<AiListRowViewModel> Rows { get; } = new();

    /// <summary>Label for a new column the user is adding.</summary>
    [ObservableProperty]
    private string newColumnLabel = "";

    public string ColumnCountLabel => $"{Columns.Count} of {AiListDsl.MaxFields} columns";

    /// <summary>What the model charged for the suggestion, for the "powered by" byline.</summary>
    [ObservableProperty]
    private string? usageLabel;

    // ── Commands ────────────────────────────────────────────────────────────────

    protected override void NotifyAiCommandsChanged()
    {
        SuggestCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleOpen()
    {
        IsOpen = !IsOpen;
        if (IsOpen) _ = EnsureStatusLoadedAsync();
    }

    private bool CanSuggest() => CanRunAi && !string.IsNullOrWhiteSpace(Description);

    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync(CancellationToken ct)
    {
        // Pre-flight. A 422 for an over-long input would cost nothing, but the
        // same check also gives an instant answer instead of a round-trip.
        if (!AiFeatureLimits.TryValidateInput(AiFeature.PoweredTemplate, Description, out var inputError))
        {
            RejectLocally(inputError!);
            return;
        }

        var result = await RunAiAsync(
            "Drafting a list from your description…",
            token => Session.Api.AiSuggestAsync(AiFeature.PoweredTemplate, Description.Trim(), ct: token),
            r => r.Quota,
            ct);

        if (result is null) return;

        if (result.Artifact.AsList() is not { } list)
        {
            Notice = new AiNotice(AiNoticeKind.ModelDeclined,
                $"Expected a list but the AI returned a \"{result.Artifact.Kind}\". " +
                "That attempt still counted against today's allowance — try describing the list differently.",
                Spent: true);
            return;
        }

        LoadPreview(list);
        UsageLabel = result.Usage?.Display;
        Notice = AiNotice.Info("Draft ready. Edit the columns and starter rows below, then create the list.");
    }

    private void LoadPreview(AiListArtifact list)
    {
        _dsl = AiListDsl.FromJson(list.Dsl) ?? AiListDsl.Empty();
        _dslName = _dsl.Name;
        _dslDescription = _dsl.Description;

        ListTitle = list.Title;
        ListDescription = list.Description ?? "";

        // The types this artifact actually uses, so a per-column picker can
        // offer them without this client inventing DSL types it hasn't seen.
        var artifactTypes = _dsl.Fields.Select(f => f.Type).ToList();

        Columns.Clear();
        foreach (var field in _dsl.Fields)
            Columns.Add(new AiListColumnViewModel(field, artifactTypes));

        Rows.Clear();
        foreach (var row in list.Rows)
            Rows.Add(AiListRowViewModel.FromWire(Columns, row));

        HasPreview = true;
        OnPropertyChanged(nameof(ColumnCountLabel));
        AddColumnCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Discard()
    {
        HasPreview = false;
        Columns.Clear();
        Rows.Clear();
        ListTitle = "";
        ListDescription = "";
        UsageLabel = null;
        NewColumnLabel = "";
        _dsl = null;
        ClearNotice();
        OnPropertyChanged(nameof(ColumnCountLabel));
        AddColumnCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveColumn(AiListColumnViewModel column)
    {
        if (column is null || !Columns.Remove(column)) return;

        // Keep the table rectangular: every row loses the matching cell.
        foreach (var row in Rows)
            row.RemoveCellFor(column);

        OnPropertyChanged(nameof(ColumnCountLabel));
        AddColumnCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddColumn() => HasPreview && Columns.Count < AiListDsl.MaxFields;

    [RelayCommand(CanExecute = nameof(CanAddColumn))]
    private void AddColumn()
    {
        var label = NewColumnLabel.Trim();
        if (label.Length == 0)
        {
            RejectLocally("Give the new column a label first.");
            return;
        }

        // Legal-key rule mirrored from the DSL contract so an illegal key can't
        // reach /generate — a rejected /generate costs a unit.
        var key = AiListDsl.KeyFromLabel(label);
        if (key is null)
        {
            RejectLocally($"\"{label}\" doesn't produce a usable column key — use letters, digits, spaces, - or _.");
            return;
        }

        if (Columns.Any(c => c.Key == key))
        {
            RejectLocally($"There's already a column with the key \"{key}\".");
            return;
        }

        var column = AiListColumnViewModel.NewColumn(key, label, Columns.Select(c => c.Type));
        Columns.Add(column);

        foreach (var row in Rows)
            row.AddCellFor(column);

        NewColumnLabel = "";
        ClearNotice();
        OnPropertyChanged(nameof(ColumnCountLabel));
        AddColumnCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddRow()
    {
        if (Columns.Count == 0) return;
        Rows.Add(AiListRowViewModel.Blank(Columns));
    }

    [RelayCommand]
    private void RemoveRow(AiListRowViewModel row)
    {
        if (row is not null) Rows.Remove(row);
    }

    private bool CanConfirm() => CanRunAi && HasPreview && Columns.Count > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken ct)
    {
        if (!TryBuildArtifact(out var artifact, out var error))
        {
            RejectLocally(error!);
            return;
        }

        var result = await RunAiAsync(
            "Creating your list…",
            token => Session.Api.AiGenerateAsync(AiFeature.PoweredTemplate, artifact!, ct: token),
            r => r.Quota,
            ct);

        if (result is null) return;

        // The /generate envelope is unverified, so the id is a hint. Prove the
        // list exists with a fresh GET before telling the host to open it.
        if (result.Created.ListId is { Length: > 0 } listId)
        {
            var created = await RunApiAsync("Loading the new list…", token => Session.Api.GetListAsync(listId, token), ct);

            RefreshCommand?.Execute(null);

            if (created is not null)
            {
                Notice = AiNotice.Info($"Created \"{created.Title}\".");
                Discard();
                IsOpen = false;
                OpenListCommand?.Execute(created);
                return;
            }

            // Written, but the read-back failed — don't claim more than that.
            Notice = AiNotice.Info("The list was created, but loading it back failed. It's in the list browser on the left.");
            Discard();
            return;
        }

        // No id came back. Something was probably written, so refresh rather
        // than assert either outcome.
        RefreshCommand?.Execute(null);
        Notice = new AiNotice(AiNoticeKind.Error,
            "The server accepted the list but didn't return its id. Check the list browser on the left before trying again — a second attempt would create a duplicate and spend another AI credit.");
    }

    /// <summary>
    /// Build the artifact to persist, running every check that would otherwise
    /// come back as a 422 from a <c>/generate</c> that already cost a unit.
    /// </summary>
    private bool TryBuildArtifact(out AiArtifact? artifact, out string? error)
    {
        artifact = null;
        error = null;

        if (string.IsNullOrWhiteSpace(ListTitle))
        {
            error = "Give the list a title before creating it.";
            return false;
        }

        if (Columns.Count == 0)
        {
            error = "A list needs at least one column.";
            return false;
        }

        if (Columns.Count > AiListDsl.MaxFields)
        {
            error = $"A list schema takes at most {AiListDsl.MaxFields} columns (there are {Columns.Count}).";
            return false;
        }

        var blankLabel = Columns.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Label));
        if (blankLabel is not null)
        {
            error = $"The \"{blankLabel.Key}\" column needs a label.";
            return false;
        }

        var duplicate = Columns.GroupBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            error = $"Two columns share the key \"{duplicate.Key}\".";
            return false;
        }

        var illegal = Columns.FirstOrDefault(c => !AiListDsl.IsValidKey(c.Key));
        if (illegal is not null)
        {
            error = $"The column key \"{illegal.Key}\" isn't valid (lowercase letters, digits, - and _, starting with a letter).";
            return false;
        }

        var fields = Columns.Select(c => c.ToField()).ToList();
        var dsl = (_dsl ?? AiListDsl.Empty()).ToElement(
            _dslName ?? ListTitle.Trim(),
            _dslDescription ?? (ListDescription.Length > 0 ? ListDescription.Trim() : null),
            fields);

        // An all-blank row is noise; the server drops rows that fail schema
        // validation anyway, so don't send them in the first place.
        var rows = Rows
            .Where(r => !r.IsEmpty)
            .Select(r => r.ToWire(Columns))
            .ToList();

        artifact = AiArtifact.FromPayload(new AiListArtifactPayload
        {
            Title = ListTitle.Trim(),
            Description = ListDescription.Length > 0 ? ListDescription.Trim() : null,
            Dsl = dsl,
            Rows = rows
        });

        return true;
    }
}
