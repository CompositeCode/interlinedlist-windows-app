namespace InterlinedList.Models;

/// <summary>
/// A list owned by someone else that the current user has been granted access to
/// (GET /api/lists/watching). Role is "collaborator" / "viewer" / etc. The
/// current user can read its rows via the normal list-data endpoint (verified
/// live 2026-07-31).
/// </summary>
public sealed class WatchedList
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public string? Role { get; init; }
    public ApiUser? User { get; init; }

    public string OwnerHandle => User is null ? string.Empty : $"@{User.Username}";
    public string RoleLabel => string.IsNullOrEmpty(Role) ? "viewer" : Role;
}
