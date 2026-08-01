namespace InterlinedList.Models;

/// <summary>
/// A user entry in a followers / following / follow-requests list.
/// FollowId + Status are present on relationship-scoped lists (a follower row
/// carries the follow edge's id so it can be approved/rejected/removed).
/// </summary>
public sealed class FollowUser
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public string? FollowId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
    public string Handle => $"@{Username}";
}
