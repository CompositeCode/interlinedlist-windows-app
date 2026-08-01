namespace InterlinedList.Models;

public sealed class OrganizationSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public int MemberCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    // Only present when this came from "my organizations" (GET api/user/organizations).
    public string? Role { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
}
