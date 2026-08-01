namespace InterlinedList.Models;

/// <summary>
/// A person granted shared access to a list (a "watcher") or a document (a
/// "collaborator"). Same wire shape for both (verified live 2026-08-01):
/// { id, userId, role, createdAt, user }. Remove using <see cref="UserId"/>
/// (the DELETE routes are keyed by user id, not the edge id).
/// </summary>
public sealed class Collaborator
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string? Role { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public ApiUser? User { get; init; }

    public string DisplayNameOrUsername => User?.DisplayName ?? User?.Username ?? "unknown";
    public string Handle => User is null ? string.Empty : $"@{User.Username}";
    public string RoleLabel => string.IsNullOrEmpty(Role) ? "watcher" : Role;
}
