using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One column in a <see cref="ListSchema"/>. <c>key</c>, <c>type</c> and
/// <c>label</c> are always required; everything else is optional and is written
/// only when non-null — a naive serializer that emits <c>"options": null</c>
/// breaks a select column outright (verified live 2026-09-16:
/// <c>400 Invalid schema: Field 's' (type: select) must have an 'options' array</c>),
/// so the WhenWritingNull attributes below are load-bearing, not cosmetic.
///
/// Row data is keyed by <see cref="Key"/>, never by <see cref="Label"/>.
/// On the wire each field is stored as a <see cref="ListProperty"/> row
/// (key → propertyKey, label → propertyName, type → propertyType).
/// </summary>
public sealed class ListField
{
    /// <summary>Machine key stored in each row's rowData. Unique within the schema.</summary>
    public required string Key { get; init; }

    /// <summary>One of <see cref="ListFieldType"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Column header shown in forms and tables.</summary>
    public required string Label { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Required { get; init; }

    /// <summary>
    /// Pre-filled value for new rows. Reads come back as a <see cref="JsonElement"/>
    /// (boxed); writes accept any plain CLR value (<c>false</c>, <c>0</c>, <c>"open"</c>).
    /// The server stores it JSON-encoded (<c>"false"</c>, <c>"\"open\""</c>) but
    /// GET …/schema hands it back decoded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Allowed values. REQUIRED for select/multiselect (an empty array is
    /// accepted by the server, but is useless, so <see cref="ListSchema.Validate"/>
    /// rejects it). Omit for <c>priority</c> to get the default
    /// low/medium/high/urgent set (verified live).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Options { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HelpText { get; init; }

    /// <summary>
    /// Extra row-write rules. Note the server also folds a select's options into
    /// the stored <c>validationRules</c>; GET …/schema surfaces them both there
    /// and in <see cref="Options"/>, and the copy under validation is ignored
    /// here because <see cref="Options"/> already round-trips it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ListFieldValidation? Validation { get; init; }

    /// <summary>false hides the column by default (server default: true).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Visible { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ListFieldVisibility? Visibility { get; init; }

    /// <summary>Defaults to the field's position in the array.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayOrder { get; init; }

    [JsonIgnore]
    public bool IsRequired => Required == true;

    [JsonIgnore]
    public bool IsVisible => Visible != false;

    /// <summary>Options to offer in a row editor, including priority's implicit four.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveOptions =>
        Options is { Count: > 0 } ? Options
        : Type == ListFieldType.Priority ? ListFieldType.PriorityOptions
        : [];

    [JsonIgnore]
    public string TypeDisplayName => ListFieldType.DisplayName(Type);

    /// <summary>True when this column survives a non-destructive <c>properties</c> edit.</summary>
    [JsonIgnore]
    public bool SupportsPropertiesEdit => ListFieldType.SupportsPropertiesEdit(Type);
}
