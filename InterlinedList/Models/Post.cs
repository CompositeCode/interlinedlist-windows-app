namespace InterlinedList.Models;

public enum StreamColor { Teal, Green, Amber }

public sealed class Post
{
    public required int Id { get; init; }
    public required StreamColor Stream { get; init; }
    public required string StreamName { get; init; }
    public required string Author { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Body { get; init; }
    public required int Digs { get; init; }
    public bool IsLive { get; init; }

    public string TimeFormatted => Timestamp.ToUniversalTime().ToString("HH:mm:ss'Z'");
}
