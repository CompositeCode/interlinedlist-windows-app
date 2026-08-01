namespace InterlinedList.Models;

public sealed class Pagination
{
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
    public bool HasMore { get; init; }
}
