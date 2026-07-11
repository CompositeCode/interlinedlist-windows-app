namespace InterlinedList.Models;

public sealed class Message
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public bool PubliclyVisible { get; init; }
    public required string UserId { get; init; }
    public string? ParentId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int DigCount { get; init; }
    public int PushCount { get; init; }
    public bool DugByMe { get; init; }
    public ApiUser? User { get; init; }
    public List<string>? ImageUrls { get; init; }
    public List<string>? Tags { get; init; }

    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");
    public string AuthorDisplayName => User?.DisplayName ?? User?.Username ?? "unknown";
    public string AuthorHandle => User is null ? string.Empty : $"@{User.Username}";
}
