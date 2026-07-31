namespace InterlinedList.Models;

/// <summary>
/// A person the current user can DM (GET /api/dm/recipients) and the
/// "otherUser" identity embedded in a thread payload.
/// </summary>
public sealed class DmRecipient
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
    public string Handle => $"@{Username}";
}
