namespace InterlinedList.Models;

public sealed class DocumentFolderEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
}
