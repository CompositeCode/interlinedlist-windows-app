namespace InterlinedList.Models;

public sealed class DocumentSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? FolderId { get; init; }
    public bool IsPublic { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
