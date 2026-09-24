namespace InterlinedList.Models;

/// <summary>
/// Request shape for <c>POST /api/messages</c>. A record rather than a long
/// positional parameter list because the endpoint keeps growing optional fields
/// (reply, schedule, media, push/quote, tags) and every caller only ever sets
/// two or three of them.
/// </summary>
/// <remarks>
/// The client serializes this by hand (see
/// <c>InterlinedApiClient.PostMessageAsync(NewMessage, CancellationToken)</c>)
/// and omits the optional keys it hasn't been given, rather than posting
/// explicit nulls for fields the caller never touched.
/// </remarks>
public sealed record NewMessage
{
    public string Content { get; init; } = "";

    /// <summary>
    /// Normally the user's <c>defaultPubliclyVisible</c> preference — but a Push
    /// or Quote is <b>always</b> public per <c>/help/messages</c>, so those paths
    /// hard-code <c>true</c>.
    /// </summary>
    public bool PubliclyVisible { get; init; } = true;

    public bool CrossPostToBluesky { get; init; }
    public bool CrossPostToTwitter { get; init; }
    public bool CrossPostToLinkedIn { get; init; }

    /// <summary>Which Mastodon identity to syndicate through, if any.</summary>
    public string? MastodonProviderIds { get; init; }

    /// <summary>Set to turn this into a reply.</summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Set to turn this into a Push (repost) when <see cref="Content"/> is empty,
    /// or a Quote when it isn't — the API has one field for both.
    /// </summary>
    public string? PushedMessageId { get; init; }

    /// <summary>Defer publication; the post lands in <c>/api/messages/scheduled</c>.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }

    public IReadOnlyList<string>? ImageUrls { get; init; }
    public IReadOnlyList<string>? VideoUrls { get; init; }
}
