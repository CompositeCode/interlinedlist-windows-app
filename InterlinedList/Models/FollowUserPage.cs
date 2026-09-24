namespace InterlinedList.Models;

/// <summary>
/// One page of a followers / following list, keeping the pagination block the
/// endpoint returns so a caller can page instead of silently taking the
/// server's default first 50.
/// </summary>
public sealed class FollowUserPage
{
    public required List<FollowUser> Users { get; init; }

    /// <summary>Total matching edges, as reported by the server.</summary>
    public int Total { get; init; }

    public bool HasMore { get; init; }

    public static FollowUserPage Empty => new() { Users = [], Total = 0, HasMore = false };
}
