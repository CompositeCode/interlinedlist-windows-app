using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>One entry in the column editor's type picker.</summary>
/// <param name="Value">The wire value (<see cref="ListFieldType"/>).</param>
/// <param name="DisplayName">Human label.</param>
/// <param name="PropertiesEditable">
/// False for the six richer types the non-destructive <c>properties</c> PUT
/// rejects — picking one of those costs safe editing on that list for good.
/// </param>
public sealed record ListColumnTypeOption(string Value, string DisplayName, bool PropertiesEditable)
{
    /// <summary>Picker caption; the six rebuild-only types are flagged in the list itself.</summary>
    public string Caption => PropertiesEditable ? DisplayName : DisplayName + "  (rebuild only)";
}

/// <summary>
/// One column being edited in the builder. Deliberately a mutable draft rather
/// than the immutable <see cref="ListField"/>/<see cref="ListPropertyUpdate"/>
/// wire types: the editor needs half-finished state (an options box mid-typing,
/// a default that doesn't parse yet) that those types can't legally hold.
///
/// It carries two things it never edits — <see cref="Validation"/> and
/// <see cref="Visibility"/> (owned by #19/#20) — so that emitting a DSL rebuild
/// from this draft doesn't silently drop rules the user never saw. The
/// non-destructive <c>properties</c> shape can't express either, and leaves both
/// untouched (live-verified 2026-09-16).
/// </summary>
public partial class ListColumnDraftViewModel : ObservableObject
{
    /// <summary>All twelve types, richer ones flagged, for the picker.</summary>
    public static IReadOnlyList<ListColumnTypeOption> TypeOptions { get; } =
        ListFieldType.All
            .Select(t => new ListColumnTypeOption(t, ListFieldType.DisplayName(t),
                                                  ListFieldType.SupportsPropertiesEdit(t)))
            .ToList();

    private bool _keyEditedByHand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyNote))]
    private string key = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiresOptions))]
    [NotifyPropertyChangedFor(nameof(SupportsPropertiesEdit))]
    [NotifyPropertyChangedFor(nameof(TypeWarning))]
    [NotifyPropertyChangedFor(nameof(HasTypeWarning))]
    [NotifyPropertyChangedFor(nameof(DefaultHint))]
    private string type = ListFieldType.Text;

    [ObservableProperty]
    private string label = "";

    [ObservableProperty]
    private bool isRequired;

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private string defaultValueText = "";

    [ObservableProperty]
    private string placeholder = "";

    [ObservableProperty]
    private string helpText = "";

    /// <summary>Comma-separated; mandatory for select/multiselect.</summary>
    [ObservableProperty]
    private string optionsText = "";

    /// <summary>Inline, per-column error — set by the editor before/after a save attempt.</summary>
    [ObservableProperty]
    private string? issueMessage;

    /// <summary>Stored column id, when this draft came from a saved list. Null = a new column.</summary>
    public string? PropertyId { get; private init; }

    /// <summary>The key this column was saved under; null for a new column.</summary>
    public string? StoredKey { get; private init; }

    /// <summary>The type this column was saved with; null for a new column.</summary>
    public string? StoredType { get; private init; }

    /// <summary>The label it was saved with — a rename is a safe, non-destructive edit.</summary>
    public string? StoredLabel { get; private init; }

    /// <summary>Its saved position, so a reorder can be reported as one.</summary>
    public int? StoredOrder { get; private init; }

    /// <summary>True when the label differs from the one on the server.</summary>
    public bool IsRenamed =>
        IsExistingColumn && !string.Equals((Label ?? "").Trim(), StoredLabel ?? "", StringComparison.Ordinal);

    /// <summary>True when the type differs from the one on the server.</summary>
    public bool IsRetyped => IsExistingColumn && StoredType is { } stored && stored != Type;

    /// <summary>True once at least one row holds a value for <see cref="StoredKey"/>.</summary>
    public bool HasRowData { get; set; }

    /// <summary>Rules the editor carries but doesn't edit (#19).</summary>
    public ListFieldValidation? Validation { get; private init; }

    /// <summary>Conditional visibility the editor carries but doesn't edit (#20).</summary>
    public ListFieldVisibility? Visibility { get; private init; }

    public bool IsExistingColumn => StoredKey is not null;

    /// <summary>
    /// The server refuses a key change on a saved column
    /// (<c>400 propertyKey cannot change for an existing property; rename
    /// propertyName instead</c>, live-verified), so the box is locked instead of
    /// letting the user type something that can only fail.
    /// </summary>
    public bool IsKeyEditable => !IsExistingColumn;

    public bool RequiresOptions => ListFieldType.RequiresOptions(Type);

    public bool SupportsPropertiesEdit => ListFieldType.SupportsPropertiesEdit(Type);

    public string DefaultHint => ListFieldDefaults.EditorHint(Type);

    public string? KeyNote => IsExistingColumn
        ? $"Key '{StoredKey}' is fixed — rename the label instead."
        : null;

    /// <summary>
    /// The warning #18 has to show at the MOMENT a richer type is picked: those
    /// six types can never go through the non-destructive edit, so every later
    /// change to this list has to be a full column rebuild.
    /// </summary>
    public string? TypeWarning
    {
        get
        {
            if (!SupportsPropertiesEdit)
                return $"{ListFieldType.DisplayName(Type)} can only be saved by rebuilding every column. "
                     + $"Safe single-column edits ({string.Join(", ", ListFieldType.PropertiesEditable)}) "
                     + "stop being possible on this list.";

            if (IsExistingColumn && StoredType is { } stored && stored != Type)
                return $"Changing {StoredKey} from {ListFieldType.DisplayName(stored)} to "
                     + $"{ListFieldType.DisplayName(Type)} does not convert the values already in rows "
                     + "— they stay as they are and will fail validation the next time that row is saved.";

            return null;
        }
    }

    public bool HasTypeWarning => TypeWarning is not null;

    /// <summary>Options as the wire wants them; also fills in priority's implicit four.</summary>
    public List<string> OptionList =>
        OptionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>A blank draft, ready for the user to fill in.</summary>
    public static ListColumnDraftViewModel ForNewColumn() => new();

    /// <summary>
    /// A draft of a saved column. <paramref name="stored"/> supplies the id the
    /// non-destructive edit needs to update in place (it only exists on
    /// <c>GET /api/lists/{id}</c> → <c>properties[]</c>, not in the DSL view).
    /// </summary>
    public static ListColumnDraftViewModel FromField(ListField field, ListProperty? stored)
    {
        var draft = new ListColumnDraftViewModel
        {
            PropertyId = stored?.Id,
            StoredKey = field.Key,
            StoredType = field.Type,
            StoredLabel = field.Label,
            StoredOrder = field.DisplayOrder,
            Validation = field.Validation,
            Visibility = field.Visibility,
            Key = field.Key,
            Type = field.Type,
            Label = field.Label,
            IsRequired = field.IsRequired,
            IsVisible = field.IsVisible,
            Placeholder = field.Placeholder ?? "",
            HelpText = field.HelpText ?? "",
            OptionsText = field.Options is { Count: > 0 } ? string.Join(", ", field.Options) : "",
            DefaultValueText = ListFieldDefaults.FromDecoded(field.DefaultValue)
        };
        draft._keyEditedByHand = true;
        return draft;
    }

    /// <summary>DSL form, for list creation and the destructive rebuild.</summary>
    public ListField ToField(int displayOrder)
    {
        var options = OptionList;
        return new ListField
        {
            Key = Key.Trim(),
            Type = Type,
            Label = string.IsNullOrWhiteSpace(Label) ? Key.Trim() : Label.Trim(),
            Required = IsRequired ? true : null,
            Visible = IsVisible ? null : false,
            DefaultValue = ListFieldDefaults.ToDslDefault(Type, DefaultValueText),
            Placeholder = string.IsNullOrWhiteSpace(Placeholder) ? null : Placeholder.Trim(),
            HelpText = string.IsNullOrWhiteSpace(HelpText) ? null : HelpText.Trim(),
            // priority takes its implicit low/medium/high/urgent set when options are
            // omitted, so only send its options when the user actually narrowed them.
            Options = options.Count > 0 && !IsImplicitPrioritySet(options) ? options : null,
            Validation = Validation is { IsEmpty: false } ? Validation : null,
            Visibility = Visibility?.Condition is not null ? Visibility : null,
            DisplayOrder = displayOrder
        };
    }

    /// <summary>
    /// Non-destructive form. Every field the shape can express is sent on every
    /// item, because an omitted one is CLEARED server-side — live-verified
    /// 2026-09-16: a PUT that left out isVisible/isRequired/defaultValue flipped
    /// a hidden column visible and wiped two defaults.
    /// </summary>
    public ListPropertyUpdate ToPropertyUpdate(int displayOrder) => new()
    {
        Id = PropertyId,
        PropertyKey = (StoredKey ?? Key).Trim(),
        PropertyName = string.IsNullOrWhiteSpace(Label) ? Key.Trim() : Label.Trim(),
        PropertyType = Type,
        DisplayOrder = displayOrder,
        IsVisible = IsVisible,
        IsRequired = IsRequired,
        DefaultValue = ListFieldDefaults.ToPropertyDefault(DefaultValueText),
        Placeholder = string.IsNullOrWhiteSpace(Placeholder) ? null : Placeholder.Trim(),
        HelpText = string.IsNullOrWhiteSpace(HelpText) ? null : HelpText.Trim()
    };

    /// <summary>
    /// Everything wrong with this one column that can be known without a
    /// request. Duplicate keys and the "at least one column" rule are checked by
    /// the editor across the whole set, not here.
    /// </summary>
    public List<string> LocalIssues()
    {
        var issues = new List<string>();
        var key = Key.Trim();

        if (key.Length == 0)
            issues.Add("Needs a key.");
        else if (key.Any(char.IsWhiteSpace))
            issues.Add("Key can't contain spaces.");

        if (string.IsNullOrWhiteSpace(Label))
            issues.Add("Needs a label.");

        if (!ListFieldType.IsValid(Type))
            issues.Add($"'{Type}' isn't one of the twelve column types.");

        if (RequiresOptions && OptionList.Count == 0)
            issues.Add($"{ListFieldType.DisplayName(Type)} needs at least one option.");

        if (!string.IsNullOrWhiteSpace(DefaultValueText))
        {
            if (Type == ListFieldType.Number && !ListFieldDefaults.TryParseNumber(DefaultValueText, out _))
                issues.Add("Default has to be a number.");
            if (Type == ListFieldType.Boolean && !ListFieldDefaults.TryParseBoolean(DefaultValueText, out _))
                issues.Add("Default has to be true or false.");
            if (RequiresOptions && Type == ListFieldType.Select
                && !OptionList.Contains(DefaultValueText.Trim(), StringComparer.Ordinal))
                issues.Add("Default isn't one of the options.");
        }

        return issues;
    }

    private bool IsImplicitPrioritySet(List<string> options) =>
        Type == ListFieldType.Priority
        && options.Count == ListFieldType.PriorityOptions.Count
        && options.SequenceEqual(ListFieldType.PriorityOptions, StringComparer.Ordinal);

    /// <summary>Slug a label into a usable machine key (only while the user hasn't typed one).</summary>
    private static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
        }
        return builder.ToString().Trim('_');
    }

    partial void OnKeyChanged(string value) => _keyEditedByHand = true;

    partial void OnLabelChanged(string value)
    {
        if (_keyEditedByHand || IsExistingColumn) return;

        var slug = Slug(value);
        Key = slug;
        // Key's setter flags the key as hand-edited; undo that so the slug keeps following.
        _keyEditedByHand = false;
    }

    partial void OnTypeChanged(string value)
    {
        // priority carries an implicit option set; showing it makes the picker honest
        // and lets the user narrow or reword it.
        if (value == ListFieldType.Priority && OptionList.Count == 0)
            OptionsText = string.Join(", ", ListFieldType.PriorityOptions);
    }
}
