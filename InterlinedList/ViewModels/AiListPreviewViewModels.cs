using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// One editable column in a Powered Template preview. Surfaces exactly the
/// three attributes #14 asks for — type, label, required — and nothing else.
///
/// <b><see cref="Key"/> is deliberately not editable.</b> Every starter row
/// addresses its cells by field key, so letting a user retype a key would
/// silently orphan that column's data. Labels are what people actually want to
/// change; a key is only ever chosen once, when a column is added, and is
/// slugified into the documented <c>^[a-z][a-z0-9_-]*$</c> shape at that point.
/// </summary>
public sealed partial class AiListColumnViewModel : ObservableObject
{
    private readonly AiListDslField _source;

    public AiListColumnViewModel(AiListDslField source, IEnumerable<string>? extraTypes = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

        Key = source.Key;
        label = source.Label.Length > 0 ? source.Label : source.Key;
        type = source.Type;
        required = source.Required;

        // Offer the types this client has actually seen the server emit, plus
        // whatever types this artifact already uses — never an invented one.
        // The DSL is only partially reverse engineered (CLAUDE.md), and a type
        // the server rejects would cost a quota unit to discover.
        AvailableTypes = AiListDslField.ObservedTypes
            .Concat(extraTypes ?? Array.Empty<string>())
            .Append(source.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Stable field key. Fixed for the lifetime of the column — see the type remarks.</summary>
    public string Key { get; }

    [ObservableProperty]
    private string label;

    [ObservableProperty]
    private string type;

    [ObservableProperty]
    private bool required;

    public IReadOnlyList<string> AvailableTypes { get; }

    /// <summary>
    /// A note when the model attached DSL rules this editor doesn't show
    /// (<c>options</c>, <c>defaultValue</c>, a <c>visibility.condition</c>).
    /// They're round-tripped untouched, and saying so beats a user wondering
    /// where the dropdown choices went.
    /// </summary>
    public string? ExtraRulesNote
    {
        get
        {
            if (!_source.HasExtraRules) return null;

            var options = _source.Options;
            return options.Count > 0
                ? $"choices: {string.Join(" / ", options)}"
                : "has extra rules (kept as-is)";
        }
    }

    public bool HasExtraRules => _source.HasExtraRules;

    /// <summary>Project the edits back onto the source field, preserving its unparsed members.</summary>
    public AiListDslField ToField() => new()
    {
        Key = Key,
        Type = Type,
        Label = Label,
        Required = Required,
        DisplayOrder = _source.DisplayOrder,
        Raw = _source.Raw
    };

    /// <summary>A column the user added by hand. Its raw element is empty, so it has no hidden rules.</summary>
    public static AiListColumnViewModel NewColumn(string key, string label, IEnumerable<string>? extraTypes = null)
        => new(new AiListDslField
        {
            Key = key,
            Label = label,
            Type = AiListDslField.DefaultType,
            Required = false,
            DisplayOrder = int.MaxValue,
            Raw = default
        }, extraTypes);
}

/// <summary>
/// One cell of a starter row.
///
/// <b>Unedited cells round-trip byte-for-byte.</b> The preview has to show
/// values as text (a WPF TextBox holds a string, not a JSON number), but
/// re-serializing "5" back into JSON is a guess about whether it was
/// <c>5</c>, <c>5.0</c> or <c>"5"</c>. So this keeps the original
/// <see cref="JsonElement"/> and only re-types a cell the user actually
/// changed — which is most of them, never.
/// </summary>
public sealed partial class AiListCellViewModel : ObservableObject
{
    private readonly JsonElement _original;
    private readonly string _originalText;

    public AiListCellViewModel(string columnKey, JsonElement? original)
    {
        ColumnKey = columnKey;
        _original = original ?? default;
        _originalText = ToText(_original);
        text = _originalText;
    }

    public string ColumnKey { get; }

    [ObservableProperty]
    private string text;

    public bool WasEdited => !string.Equals(Text, _originalText, StringComparison.Ordinal);

    /// <summary>
    /// The value to send. An untouched cell returns its original element
    /// verbatim; an edited one is re-typed from <paramref name="columnType"/>,
    /// falling back to a string rather than dropping input the client can't
    /// parse as a number.
    /// </summary>
    public object? ToWireValue(string columnType)
    {
        if (!WasEdited)
            return _original.ValueKind == JsonValueKind.Undefined ? null : _original;

        if (string.IsNullOrWhiteSpace(Text)) return null;

        var value = Text.Trim();

        switch (columnType)
        {
            case "number":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    return whole;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                    return real;
                // Keep what they typed. The server validates starter rows and
                // drops the ones that fail rather than failing the artifact, so
                // a bad number costs a row, not the list.
                return value;

            case "boolean":
            case "checkbox":
                return bool.TryParse(value, out var flag) ? flag : value;

            default:
                return value;
        }
    }

    private static string ToText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => "",
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => element.GetRawText(),
        // An array/object inside a starter row cell has never been observed;
        // show its JSON so at least nothing is hidden from the user.
        _ => element.GetRawText()
    };
}

/// <summary>
/// One starter row, as a cell per column in column order. The row's own cells
/// are rebuilt whenever a column is added or removed, so the table stays
/// rectangular and the header always lines up with the body.
/// </summary>
public sealed class AiListRowViewModel
{
    public ObservableCollection<AiListCellViewModel> Cells { get; } = new();

    public static AiListRowViewModel FromWire(
        IReadOnlyList<AiListColumnViewModel> columns,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        var row = new AiListRowViewModel();
        foreach (var column in columns)
        {
            JsonElement? value = values is not null && values.TryGetValue(column.Key, out var found) ? found : null;
            row.Cells.Add(new AiListCellViewModel(column.Key, value));
        }
        return row;
    }

    public static AiListRowViewModel Blank(IReadOnlyList<AiListColumnViewModel> columns)
    {
        var row = new AiListRowViewModel();
        foreach (var column in columns)
            row.Cells.Add(new AiListCellViewModel(column.Key, null));
        return row;
    }

    public void AddCellFor(AiListColumnViewModel column)
        => Cells.Add(new AiListCellViewModel(column.Key, null));

    public void RemoveCellFor(AiListColumnViewModel column)
    {
        var cell = Cells.FirstOrDefault(c => c.ColumnKey == column.Key);
        if (cell is not null) Cells.Remove(cell);
    }

    /// <summary>True when every cell is blank — an empty row is worth dropping rather than sending.</summary>
    public bool IsEmpty => Cells.All(c => string.IsNullOrWhiteSpace(c.Text));

    /// <summary>
    /// The row object /generate expects: a flat map keyed by field key
    /// (verified live — starter rows came back as
    /// <c>{"title":"The Hobbit","author":"J.R.R. Tolkien","status":"Finished","rating":5}</c>,
    /// with <c>null</c> for an unset value).
    /// </summary>
    public Dictionary<string, object?> ToWire(IReadOnlyList<AiListColumnViewModel> columns)
    {
        var wire = new Dictionary<string, object?>();

        foreach (var column in columns)
        {
            var cell = Cells.FirstOrDefault(c => c.ColumnKey == column.Key);
            wire[column.Key] = cell?.ToWireValue(column.Type);
        }

        return wire;
    }
}
