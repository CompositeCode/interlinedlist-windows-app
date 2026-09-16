using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// Column/schema configuration for a <see cref="MaterializeTarget.List"/> or
/// <see cref="MaterializeTarget.Both"/> materialize. Optional — omit it to let
/// the server pick a title and derive the columns itself.
/// </summary>
public sealed class MaterializeListConfig
{
    public required string Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    public bool IsPublic { get; init; }

    /// <summary>
    /// Column definitions. Each one's <see cref="MaterializeListField.SourceKey"/>
    /// tells the server which source attribute to derive the column's values from.
    /// </summary>
    public IReadOnlyList<MaterializeListField> Fields { get; init; } = [];

    /// <summary>
    /// Seed the new list with rows derived from the source. Defaults to <c>true</c>
    /// (server-side default too); set <c>false</c> for an empty, columns-only list.
    /// </summary>
    public bool IncludeData { get; init; } = true;
}
