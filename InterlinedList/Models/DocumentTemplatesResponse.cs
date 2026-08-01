namespace InterlinedList.Models;

public sealed class DocumentTemplatesResponse
{
    public required List<DocumentTemplate> Templates { get; init; }
    public string? TemplatesFolderId { get; init; }
}
