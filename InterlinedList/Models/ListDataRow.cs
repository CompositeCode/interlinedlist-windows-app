using System.Text.Json;

namespace InterlinedList.Models;

public sealed class ListDataRow
{
    public required string Id { get; init; }
    public required string ListId { get; init; }
    public required Dictionary<string, JsonElement> RowData { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Read-only "key: value, key2: value2" preview — good enough since rows are freeform JSON with no schema.</summary>
    public string DisplaySummary =>
        string.Join(", ", RowData.Select(kv => $"{kv.Key}: {kv.Value}"));
}
