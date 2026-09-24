using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>How a cell renders in the row grid.</summary>
public static class ListRowCellKind
{
    public const string Text = "Text";
    public const string Boolean = "Boolean";
    public const string Chips = "Chips";
}

/// <summary>One column's value on one row, already shaped for display.</summary>
public sealed class ListRowCellViewModel
{
    public required string Label { get; init; }
    public required string Kind { get; init; }

    /// <summary>Rendered value, or an em dash when the row has no value for the column.</summary>
    public required string Text { get; init; }

    public bool Flag { get; init; }

    /// <summary>False when the row simply has no value here (a null or a missing key).</summary>
    public bool HasValue { get; init; }

    public IReadOnlyList<string> Chips { get; init; } = [];
}

/// <summary>
/// A row as the grid shows it: one typed cell per visible column instead of the
/// JSON blob the view used to print. Keys the schema doesn't cover are summarised
/// separately rather than hidden, because a destructive column rebuild leaves
/// exactly those behind (values stay in <c>rowData</c>, no longer shown or
/// validated — live-verified 2026-09-16) and pretending they're gone is how data
/// gets lost quietly.
/// </summary>
public sealed class ListRowViewModel
{
    private const string Empty = "—";

    public ListDataRow Row { get; }
    public string Id => Row.Id;
    public bool HasSchema { get; }
    public IReadOnlyList<ListRowCellViewModel> Cells { get; }

    /// <summary>The old freeform "key: value, …" line — still the right thing for a schema-less list.</summary>
    public string RawSummary { get; }

    public string? OrphanSummary { get; }

    private ListRowViewModel(ListDataRow row, bool hasSchema,
                             IReadOnlyList<ListRowCellViewModel> cells,
                             string rawSummary, string? orphanSummary)
    {
        Row = row;
        HasSchema = hasSchema;
        Cells = cells;
        RawSummary = rawSummary;
        OrphanSummary = orphanSummary;
    }

    public static ListRowViewModel Create(ListDataRow row, ListSchema? schema)
    {
        var rawSummary = string.Join(", ", row.RowData.Select(kv =>
            $"{kv.Key}: {ListFieldConditionEvaluator.AsText(kv.Value)}"));

        if (schema is null || !schema.HasFields)
            return new ListRowViewModel(row, false, [], rawSummary, null);

        var cells = new List<ListRowCellViewModel>();
        foreach (var field in schema.FieldsInDisplayOrder)
        {
            if (!field.IsVisible) continue;

            // Conditional columns are evaluated per row — the server stores the
            // rule but never applies it.
            if (!ListFieldConditionEvaluator.IsShown(field,
                    key => row.RowData.TryGetValue(key, out var watched) ? watched : null))
                continue;

            cells.Add(BuildCell(field, row.RowData.TryGetValue(field.Key, out var value) ? value : null));
        }

        var orphans = row.RowData.Keys.Where(k => schema.FindField(k) is null).ToList();
        var orphanSummary = orphans.Count == 0
            ? null
            : $"No column for: {string.Join(", ", orphans)}";

        return new ListRowViewModel(row, true, cells, rawSummary, orphanSummary);
    }

    private static ListRowCellViewModel BuildCell(ListField field, JsonElement? value)
    {
        var hasValue = value is { } element
                       && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        if (field.Type == ListFieldType.Boolean)
        {
            var flag = hasValue && value!.Value.ValueKind == JsonValueKind.True;
            return new ListRowCellViewModel
            {
                Label = field.Label,
                Kind = ListRowCellKind.Boolean,
                Text = hasValue ? (flag ? "true" : "false") : Empty,
                Flag = flag,
                HasValue = hasValue
            };
        }

        if (field.Type == ListFieldType.MultiSelect)
        {
            var chips = hasValue && value!.Value.ValueKind == JsonValueKind.Array
                ? value.Value.EnumerateArray().Select(e => ListFieldConditionEvaluator.AsText(e)).ToList()
                : [];
            return new ListRowCellViewModel
            {
                Label = field.Label,
                Kind = ListRowCellKind.Chips,
                Text = chips.Count > 0 ? string.Join(", ", chips) : Empty,
                HasValue = chips.Count > 0,
                Chips = chips
            };
        }

        var text = hasValue ? ListFieldConditionEvaluator.AsText(value!.Value) : Empty;
        return new ListRowCellViewModel
        {
            Label = field.Label,
            Kind = ListRowCellKind.Text,
            Text = text.Length == 0 ? Empty : text,
            HasValue = hasValue && text.Length > 0
        };
    }
}
