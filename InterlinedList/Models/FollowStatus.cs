namespace InterlinedList.Models;

/// <summary>
/// GET /api/follow/{userId}/status — the caller's relationship to a target user.
/// Status is "approved" / "pending" / null; IsPending flags a follow request the
/// caller has sent that the (private) target hasn't approved yet.
/// </summary>
public sealed class FollowStatus
{
    public string? Status { get; init; }
    public bool IsFollowing { get; init; }
    public bool IsPending { get; init; }
}
