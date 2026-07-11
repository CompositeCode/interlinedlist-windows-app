namespace InterlinedList.Models;

public sealed class DocumentSearchResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
}
