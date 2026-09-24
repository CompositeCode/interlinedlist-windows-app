using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>How list/row sources render as a document body.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterializeListStyle>))]
public enum MaterializeListStyle
{
    [JsonStringEnumMemberName("numbered")]
    Numbered,

    [JsonStringEnumMemberName("bulleted")]
    Bulleted
}

/// <summary>How each row's fields are laid out inside the document.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterializeRowDataStyle>))]
public enum MaterializeRowDataStyle
{
    [JsonStringEnumMemberName("inline")]
    Inline,

    [JsonStringEnumMemberName("sub-items")]
    SubItems
}

/// <summary>
/// Title/path/style configuration for a <see cref="MaterializeTarget.Doc"/> or
/// <see cref="MaterializeTarget.Both"/> materialize. Optional — omit it to let
/// the server choose.
/// </summary>
public sealed class MaterializeDocConfig
{
    public required string Title { get; init; }

    /// <summary>Folder path for the new document (same shape as a document template's relativePath).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelativePath { get; init; }

    public bool IsPublic { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterializeListStyle? ListStyle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterializeRowDataStyle? RowDataStyle { get; init; }
}
