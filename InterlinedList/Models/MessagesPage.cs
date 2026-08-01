namespace InterlinedList.Models;

public sealed class MessagesPage
{
    public required List<Message> Messages { get; init; }
    public required Pagination Pagination { get; init; }
}
