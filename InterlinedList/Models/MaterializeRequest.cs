using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// Request body for POST /api/materialize — the "Create from…" endpoint.
/// Subscriber-only. Send only ids: the server re-fetches and authorizes every
/// referenced object under the calling user (see <see cref="MaterializeSource"/>).
/// </summary>
public sealed class MaterializeRequest
{
    public required MaterializeTarget Target { get; init; }
    public required MaterializeSource Source { get; init; }

    /// <summary>Used when <see cref="Target"/> is List or Both; ignored otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterializeListConfig? ListConfig { get; init; }

    /// <summary>Used when <see cref="Target"/> is Doc or Both; ignored otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterializeDocConfig? DocConfig { get; init; }

    /// <summary>Used when <see cref="Target"/> is Message; ignored otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterializeMessageConfig? MessageConfig { get; init; }
}
