namespace InterlinedList.Models;

public sealed class ListsPage
{
    public required List<ListSummary> Lists { get; init; }
    public required Pagination Pagination { get; init; }
}
