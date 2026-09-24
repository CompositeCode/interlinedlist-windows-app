using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// What POST /api/materialize should produce. The first three targets CREATE
/// (201 with { list?, document? }); <see cref="Message"/> is the odd one out —
/// it writes nothing and returns a draft envelope instead (verified live
/// 2026-09-16), which is why the client exposes it through a separate method.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterializeTarget>))]
public enum MaterializeTarget
{
    /// <summary>Create a list from the source.</summary>
    [JsonStringEnumMemberName("list")]
    List,

    /// <summary>Create a document from the source.</summary>
    [JsonStringEnumMemberName("doc")]
    Doc,

    /// <summary>Create both a list and a document from the source.</summary>
    [JsonStringEnumMemberName("both")]
    Both,

    /// <summary>
    /// Return a post draft derived from the source. Writes nothing — publishing
    /// is a separate POST /api/messages call, where every posting gate lives.
    /// </summary>
    [JsonStringEnumMemberName("message")]
    Message
}
