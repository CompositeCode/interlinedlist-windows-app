namespace InterlinedList.Models;

/// <summary>
/// A member of an organization (GET /api/organizations/{id}/members → members[]).
/// Role is "owner" / "admin" / "member"; Active flags a soft-removed seat.
/// This endpoint accepts the bearer token (verified live 2026-07-31 — an earlier
/// note claimed it was 401-walled; that's no longer true).
/// </summary>
public sealed class OrgMember
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public string? Role { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
    public string Handle => $"@{Username}";
    public string RoleLabel => string.IsNullOrEmpty(Role) ? "member" : Role;
}
