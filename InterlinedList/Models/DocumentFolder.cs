namespace InterlinedList.Models;

public sealed class DocumentFolder
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentId { get; init; }
    public required List<DocumentFolderEntry> Documents { get; init; }
}
