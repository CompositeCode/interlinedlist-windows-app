namespace InterlinedList.Models;

/// <summary>
/// A user entry in the current user's block list (GET /api/user/blocks →
/// blockedUsers[]) or mute list (GET /api/user/mutes → mutedUsers[]).
/// </summary>
public sealed class ModeratedUser
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
    public string Handle => $"@{Username}";
}
