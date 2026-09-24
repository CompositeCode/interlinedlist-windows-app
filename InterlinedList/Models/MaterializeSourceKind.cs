using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// Which kind of object POST /api/materialize should convert. Every kind is an
/// <em>id-only</em> reference — see <see cref="MaterializeSource"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterializeSourceKind>))]
public enum MaterializeSourceKind
{
    /// <summary>One or more messages (messageIds).</summary>
    [JsonStringEnumMemberName("messages")]
    Messages,

    /// <summary>One or more whole lists, schema + rows (listIds).</summary>
    [JsonStringEnumMemberName("lists")]
    Lists,

    /// <summary>Selected rows from a single list (listId + rowIds).</summary>
    [JsonStringEnumMemberName("rows")]
    Rows,

    /// <summary>A whole document (documentId).</summary>
    [JsonStringEnumMemberName("document")]
    Document,

    /// <summary>A selection inside a document (documentId + the selected markdown).</summary>
    [JsonStringEnumMemberName("docElements")]
    DocElements
}
