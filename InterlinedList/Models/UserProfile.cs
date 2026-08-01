namespace InterlinedList.Models;

/// <summary>
/// Public profile from GET /api/users/{username}. Relationship state
/// (isFollowing / isBlocked / isMuted) is NOT reliably populated on this
/// payload — fetch it separately via the follow-status and block/mute
/// endpoints (verified live 2026-07-31: those fields came back null here).
/// </summary>
public sealed class UserProfile
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Bio { get; init; }
    public string? Avatar { get; init; }
    public string? HeaderImage { get; init; }
    public bool IsPrivate { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public int FollowerCount { get; init; }
    public int FollowingCount { get; init; }
    public int PublicListCount { get; init; }
    public int PublicMessageCount { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
    public string Handle => $"@{Username}";
}
