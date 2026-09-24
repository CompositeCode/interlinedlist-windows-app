namespace InterlinedList.Models;

/// <summary>
/// One row of GET /api/dm/conversations — "One row per conversation (grouped by
/// pairKey), newest activity first".
///
/// SHAPE CAUTION — deliberately loosely typed. This endpoint is NOT modelled in
/// the OpenAPI spec (its 200 is literally declared as a bare
/// <c>{"type":"object"}</c> with "The body of this operation is not
/// individually modelled yet") and it is NOT in the endpoint table on
/// /help/api/direct-messages either. The property set below was captured from a
/// single real, populated row on 2026-09-15:
///
/// <code>
/// {"pairKey":"15e3d575-…:c65092fa-…",
///  "otherUser":{"id":"…","username":"adron","displayName":"Adron Hall","avatar":"https://…"},
///  "lastMessageId":"93126d10-…",
///  "lastBody":"[automated recon probe …]",
///  "preview":"[automated recon probe …]",
///  "lastImageUrls":[],
///  "lastCreatedAt":"2026-07-31T22:20:32.337Z",
///  "isMine":true,
///  "unreadCount":0}
/// </code>
///
/// Because that is one row from one account, every property here is optional
/// (no <c>required</c>) so an absent or renamed key degrades to a null/default
/// instead of throwing during deserialization. There is no <c>id</c> on a
/// conversation row — <see cref="PairKey"/> is the identity, a stable sorted
/// "a:b" anchor that is identical for both directions of the conversation.
/// <see cref="IsMine"/> reports whether the LAST message was sent by the caller;
/// <see cref="OtherUser"/> is the other participant.
/// </summary>
public sealed class DmConversation
{
    public string? PairKey { get; init; }
    public DmRecipient? OtherUser { get; init; }
    public string? LastMessageId { get; init; }
    public string? LastBody { get; init; }

    /// <summary>Short markdown-stripped plaintext excerpt, meant for list rendering.</summary>
    public string? Preview { get; init; }

    public List<string>? LastImageUrls { get; init; }
    public DateTimeOffset? LastCreatedAt { get; init; }

    /// <summary>True when the caller sent the most recent message in this conversation.</summary>
    public bool IsMine { get; init; }

    public int UnreadCount { get; init; }
}
