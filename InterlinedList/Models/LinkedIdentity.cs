namespace InterlinedList.Models;

public sealed class LinkedIdentity
{
    public required string Id { get; init; }

    // Can be a simple name ("linkedin", "twitter", "bluesky") or an
    // instance-qualified compound string ("mastodon:techhub.social").
    public required string Provider { get; init; }
    public string? ProviderUsername { get; init; }
    public string? ProfileUrl { get; init; }
    public string? AvatarUrl { get; init; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
}
