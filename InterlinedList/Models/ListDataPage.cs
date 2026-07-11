namespace InterlinedList.Models;

public sealed class ListDataPage
{
    public required List<ListDataRow> Rows { get; init; }
    public required Pagination Pagination { get; init; }
}
