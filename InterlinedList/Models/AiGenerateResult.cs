using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// POST /api/ai/generate, 201 Created — the confirm half of the flow, and the
/// only one that writes. Writes are always scoped to the authenticated user;
/// nothing in the artifact controls ownership.
///
/// NOT LIVE-VERIFIED, on purpose: /generate persists content to the shared test
/// account, so it was never called during this build-out. The envelope here is
/// transcribed from the published contract, and per this repo's read-after-write
/// rule a caller should re-fetch the created resource (GET /api/lists/{id},
/// GET /api/documents/{id}, …) rather than trusting these fields. <see
/// cref="Raw"/> keeps the untouched body so a caller can inspect what actually
/// came back.
/// </summary>
public sealed class AiGenerateResult
{
    public bool Ok { get; init; }

    /// <summary>Raw wire feature echoed by the server.</summary>
    public required string Feature { get; init; }

    public required AiCreatedResources Created { get; init; }

    public required AiQuota Quota { get; init; }

    /// <summary>The whole 201 body, unparsed.</summary>
    public required JsonElement Raw { get; init; }

    public AiFeature? KnownFeature => AiFeatures.TryParse(Feature, out var feature) ? feature : null;
}

/// <summary>
/// The "created" object. The contract documents four shapes with disjoint
/// fields, so they're flattened into one type with nullable members rather than
/// a class hierarchy the client would have to guess its way into:
///
///   { listId }                                                  ← a list, or a message_series persisted as a list
///   { scheduledMessageIds, firstScheduledAt, lastScheduledAt }  ← message_series with scheduleImmediately: true
///   { documentId }                                              ← a single document
///   { folderId, documentIds[] }                                 ← a doc_series (folder of documents)
///
/// Use <see cref="Shape"/> to branch. Unverified live — see
/// <see cref="AiGenerateResult"/>.
/// </summary>
public sealed class AiCreatedResources
{
    public string? ListId { get; init; }
    public string? DocumentId { get; init; }
    public string? FolderId { get; init; }
    public List<string> DocumentIds { get; init; } = new();
    public List<string> ScheduledMessageIds { get; init; } = new();
    public DateTimeOffset? FirstScheduledAt { get; init; }
    public DateTimeOffset? LastScheduledAt { get; init; }

    /// <summary>
    /// The "created" object exactly as returned. [JsonIgnore] + private set on
    /// purpose: this is populated by <see cref="FromJson"/> after
    /// deserialization, and a `required` member the wire never carries would
    /// make System.Text.Json throw on every response.
    /// </summary>
    [JsonIgnore]
    public JsonElement Raw { get; private set; }

    /// <summary>Deserialize the documented fields, then keep the untouched element alongside them.</summary>
    internal static AiCreatedResources FromJson(JsonElement element)
    {
        var created = element.Deserialize<AiCreatedResources>(AiJson.Options) ?? new AiCreatedResources();
        created.Raw = element.Clone();
        return created;
    }

    public AiCreatedShape Shape =>
        ScheduledMessageIds.Count > 0 ? AiCreatedShape.ScheduledMessages
        : FolderId is { Length: > 0 } ? AiCreatedShape.DocumentFolder
        : DocumentId is { Length: > 0 } ? AiCreatedShape.Document
        : ListId is { Length: > 0 } ? AiCreatedShape.List
        : AiCreatedShape.Unknown;

    /// <summary>"Created 4 documents in a folder" — a line the UI can show after a confirm.</summary>
    public string Summary => Shape switch
    {
        AiCreatedShape.List => "Created a list.",
        AiCreatedShape.Document => "Created a document.",
        AiCreatedShape.DocumentFolder => $"Created a folder with {DocumentIds.Count} document(s).",
        AiCreatedShape.ScheduledMessages => $"Scheduled {ScheduledMessageIds.Count} message(s).",
        _ => "Created."
    };
}

public enum AiCreatedShape
{
    Unknown,
    List,
    Document,
    DocumentFolder,
    ScheduledMessages
}
