using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One stored column row — the wire form BEHIND the DSL. A schema field maps
/// onto it as key → propertyKey, label → propertyName, type → propertyType.
///
/// Read from two places (both verified live 2026-09-16):
/// <list type="bullet">
///   <item><c>GET /api/lists/{id}</c> → <c>data.properties[]</c> — the only way
///   to learn each column's <see cref="Id"/>, which the non-destructive
///   <c>properties</c> edit needs to update a column in place.</item>
///   <item><c>PUT /api/lists/{id}/schema</c> with a <c>properties</c> body →
///   <c>{ "properties": [ … ] }</c>, ordered by displayOrder, no data envelope.</item>
/// </list>
/// <see cref="DefaultValue"/>/<see cref="ValidationRules"/>/<see cref="VisibilityCondition"/>
/// are kept as raw JSON on purpose: the server stores defaults JSON-encoded
/// (<c>"false"</c>, <c>"\"open\""</c>, <c>"1"</c>) and folds a select's options
/// into validationRules. Echo them back verbatim rather than re-encoding them.
/// </summary>
public sealed class ListProperty
{
    public required string Id { get; init; }
    public string? ListId { get; init; }
    public required string PropertyKey { get; init; }
    public required string PropertyName { get; init; }
    public required string PropertyType { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsRequired { get; init; }
    public bool IsVisible { get; init; }
    public JsonElement? DefaultValue { get; init; }
    public JsonElement? ValidationRules { get; init; }
    public JsonElement? VisibilityCondition { get; init; }
    public string? HelpText { get; init; }
    public string? Placeholder { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonIgnore]
    public string TypeDisplayName => ListFieldType.DisplayName(PropertyType);

    /// <summary>True when this column can go through a non-destructive edit at all.</summary>
    [JsonIgnore]
    public bool SupportsPropertiesEdit => ListFieldType.SupportsPropertiesEdit(PropertyType);

    /// <summary>
    /// Project onto the request shape, carrying <see cref="Id"/> so the server
    /// updates this column in place (preserving its row data, and the
    /// validationRules / visibilityCondition the request shape can't express —
    /// verified live: a rename PUT left both intact).
    /// </summary>
    public ListPropertyUpdate ToUpdate() => new()
    {
        Id = Id,
        PropertyKey = PropertyKey,
        PropertyName = PropertyName,
        PropertyType = PropertyType,
        DisplayOrder = DisplayOrder,
        IsVisible = IsVisible,
        IsRequired = IsRequired,
        DefaultValue = DefaultValue,
        HelpText = HelpText,
        Placeholder = Placeholder
    };
}
