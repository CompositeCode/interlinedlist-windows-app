using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One item of the NON-DESTRUCTIVE <c>properties</c> body for
/// PUT /api/lists/{id}/schema. Semantics verified live 2026-09-16:
/// <list type="bullet">
///   <item>An item WITH <see cref="Id"/> updates that column in place and its
///   row data is preserved (row ids and <c>version</c> unchanged).</item>
///   <item>An item WITHOUT <see cref="Id"/> creates a new column.</item>
///   <item>A column the array OMITS is soft-deleted and its key is stripped
///   from every row — refused with <c>409</c> +
///   <c>propertiesWithData: [ … ]</c> while it still holds data unless the
///   request passes <c>?force=true</c>.</item>
///   <item><see cref="PropertyType"/> is mandatory on every item and must be
///   one of <see cref="ListFieldType.PropertiesEditable"/> (the six).</item>
///   <item><see cref="PropertyKey"/> cannot change for an existing id —
///   rename <see cref="PropertyName"/> instead.</item>
///   <item>displayOrder is authoritative by ARRAY ORDER and renumbered 0..n-1
///   server-side, so reordering means reordering the list you send.</item>
/// </list>
/// A record (unlike the repo's other wire types) so an editor can do
/// <c>property.ToUpdate() with { PropertyName = "New label" }</c>.
/// </summary>
public sealed record ListPropertyUpdate
{
    /// <summary>Existing column id — omit to CREATE a column.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    public required string PropertyKey { get; init; }
    public required string PropertyName { get; init; }

    /// <summary>One of <see cref="ListFieldType.PropertiesEditable"/> — the other six DSL types are rejected here.</summary>
    public required string PropertyType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayOrder { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsVisible { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsRequired { get; init; }

    /// <summary>Echo <see cref="ListProperty.DefaultValue"/> back verbatim; it is stored JSON-encoded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DefaultValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HelpText { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; init; }

    [JsonIgnore]
    public bool IsNew => string.IsNullOrEmpty(Id);
}
