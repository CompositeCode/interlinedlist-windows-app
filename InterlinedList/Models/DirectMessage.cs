namespace InterlinedList.Models;

/// <summary>
/// A single 1:1 direct message (matches the OpenAPI DirectMessage schema).
/// Soft-delete is per-side: sender/recipientDeletedAt hide it for only one
/// participant. ReadAt is set once the recipient opens the thread.
///
/// IMPORTANT (live-verified 2026-09-15): the API NEVER serializes
/// <see cref="SenderDeletedAt"/> / <see cref="RecipientDeletedAt"/>, even though
/// the OpenAPI schema declares them. /help/api/direct-messages states it
/// outright — "The recipient's per-side delete timestamps are never exposed: you
/// cannot tell whether the other party has trashed their own copy." A real row
/// from GET /api/dm?folder=deleted carries exactly these keys and nothing more:
/// body, createdAt, id, imageUrls, pairKey, preview, readAt, recipient,
/// recipientId, sender, senderId. So both properties stay on the model to match
/// the documented schema, but they are always null in practice — whether a
/// message is trashed is knowable only from WHICH folder returned it, which is
/// why the Deleted tab (not a per-message flag) owns the restore affordance.
/// </summary>
public sealed class DirectMessage
{
    public required string Id { get; init; }
    public required string SenderId { get; init; }
    public required string RecipientId { get; init; }
    public required string Body { get; init; }
    public List<string>? ImageUrls { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>Declared by the OpenAPI schema but never actually serialized — see the type remarks.</summary>
    public DateTimeOffset? SenderDeletedAt { get; init; }

    /// <summary>Declared by the OpenAPI schema but never actually serialized — see the type remarks.</summary>
    public DateTimeOffset? RecipientDeletedAt { get; init; }

    /// <summary>
    /// Stable sorted "a:b" anchor, identical for both directions of a
    /// conversation — the key GET /api/dm/conversations groups by. Nullable
    /// because the folder and single-message payloads include it but the thread
    /// payload's items were never observed populated (empty on the test
    /// account), so it is not assumed present everywhere.
    /// </summary>
    public string? PairKey { get; init; }

    /// <summary>Nested author identity, present on folder rows and GET /api/dm/{id}.</summary>
    public DmRecipient? Sender { get; init; }

    /// <summary>Nested addressee identity, present on folder rows and GET /api/dm/{id}.</summary>
    public DmRecipient? Recipient { get; init; }

    /// <summary>Short markdown-stripped plaintext excerpt the API supplies for list rendering.</summary>
    public string? Preview { get; init; }

    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");

    /// <summary>Absolute date+time for folder rows, which span many days (mono, per the brand's time/meta rule).</summary>
    public string DateTimeFormatted => CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm'Z'");
}
