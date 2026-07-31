namespace InterlinedList.Models;

/// <summary>
/// A single 1:1 direct message (matches the OpenAPI DirectMessage schema).
/// Soft-delete is per-side: sender/recipientDeletedAt hide it for only one
/// participant. ReadAt is set once the recipient opens the thread.
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
    public DateTimeOffset? SenderDeletedAt { get; init; }
    public DateTimeOffset? RecipientDeletedAt { get; init; }

    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");
}
