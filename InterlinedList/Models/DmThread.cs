namespace InterlinedList.Models;

/// <summary>
/// GET /api/dm/thread/{username} — the conversation with one person, oldest
/// page first. OlderCursor paginates backward into history.
/// </summary>
public sealed class DmThread
{
    public required List<DirectMessage> Items { get; init; }
    public DmRecipient? OtherUser { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsMutual { get; init; }
    public string? OlderCursor { get; init; }
}
