namespace InterlinedList.Models;

public sealed class ListSearchResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
}
