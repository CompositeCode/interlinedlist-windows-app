using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One column definition in <see cref="MaterializeListConfig.Fields"/>.
/// <para>
/// <see cref="SourceKey"/> is the load-bearing field: it names the attribute of
/// the source object this column is derived from (e.g. <c>content</c> or
/// <c>createdAt</c> for a messages source), and the server re-derives every cell
/// value through it. It does <em>not</em> accept client cell values — which is why
/// this type has no "value" member at all. <see cref="SourceKey"/> is
/// <c>null</c> for a user-added empty column, and a null there is meaningful, so
/// it is always serialized (never omitted).
/// </para>
/// <see cref="PropertyType"/> stays a plain string: the valid type vocabulary
/// belongs to the list schema DSL (PUT /api/lists/{id}/schema), which this repo
/// does not implement yet.
/// </summary>
public sealed class MaterializeListField
{
    public required string PropertyKey { get; init; }
    public required string PropertyName { get; init; }
    public required string PropertyType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsRequired { get; init; }

    /// <summary>Allowed values for an enumerated column type.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>
    /// Source attribute this column maps to, or <c>null</c> for a user-added empty
    /// column. Always written, including when null — omitting it would change the
    /// meaning of the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceKey { get; init; }

    /// <summary>A column whose values the server derives from <paramref name="sourceKey"/>.</summary>
    public static MaterializeListField FromSource(
        string propertyKey, string propertyName, string propertyType, string sourceKey,
        bool? isRequired = null, IReadOnlyList<string>? options = null) => new()
    {
        PropertyKey = propertyKey,
        PropertyName = propertyName,
        PropertyType = propertyType,
        SourceKey = string.IsNullOrWhiteSpace(sourceKey)
            ? throw new ArgumentException("Use MaterializeListField.Empty for an unmapped column.", nameof(sourceKey))
            : sourceKey,
        IsRequired = isRequired,
        Options = options
    };

    /// <summary>A user-added column with no source mapping — created empty (sourceKey null).</summary>
    public static MaterializeListField Empty(
        string propertyKey, string propertyName, string propertyType,
        bool? isRequired = null, IReadOnlyList<string>? options = null) => new()
    {
        PropertyKey = propertyKey,
        PropertyName = propertyName,
        PropertyType = propertyType,
        SourceKey = null,
        IsRequired = isRequired,
        Options = options
    };
}
