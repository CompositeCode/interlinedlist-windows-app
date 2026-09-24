using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// A feed message. Wire type for <c>GET /api/messages</c> and friends.
/// </summary>
/// <remarks>
/// Field set reconciled against live payloads captured 2026-09-16 from the test
/// account (80-message sample, chosen so the rarely-populated fields —
/// <see cref="PushedMessage"/>, <see cref="LinkMetadata"/>,
/// <see cref="CrossPostUrls"/> — appeared with real values rather than nulls).
/// </remarks>
public sealed class Message
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public bool PubliclyVisible { get; init; }
    public required string UserId { get; init; }
    public string? ParentId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int DigCount { get; init; }
    public int PushCount { get; init; }
    public bool DugByMe { get; init; }
    public ApiUser? User { get; init; }
    public List<string>? ImageUrls { get; init; }
    public List<string>? VideoUrls { get; init; }
    public List<string>? Tags { get; init; }

    /// <summary>Set when this message is a Push (repost) or Quote of another.</summary>
    public string? PushedMessageId { get; init; }

    /// <summary>
    /// The original message, nested, when this is a Push or Quote. The server
    /// sends it inline, so rendering a quoted original needs no second fetch.
    /// </summary>
    public Message? PushedMessage { get; init; }

    /// <summary>
    /// Unfurled link previews for the URLs in <see cref="Content"/>. Arrives
    /// inline on the feed, so preview cards generally need no extra call.
    /// </summary>
    public LinkMetadataEnvelope? LinkMetadata { get; init; }

    /// <summary>Where this message was syndicated to, one entry per platform.</summary>
    public List<CrossPostUrl>? CrossPostUrls { get; init; }

    /// <summary>
    /// Reply counts <b>on the syndicated copies</b> — one entry per external
    /// platform. Not the InterlinedList reply count; that is
    /// <see cref="ReplyCount"/> / <see cref="Count"/>.
    /// </summary>
    public List<PlatformReplyCount>? ReplyCounts { get; init; }

    /// <summary>When the cross-platform reply counts were last refreshed.</summary>
    public DateTimeOffset? RepliesCheckedAt { get; init; }

    /// <summary>Set on a scheduled post; null once published.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }

    /// <summary>
    /// The cross-post selection a scheduled post will use when it fires. Kept as
    /// raw JSON: it was <c>null</c> across every sampled message, so the shape is
    /// unverified and typing it strictly would be guessing.
    /// </summary>
    public JsonElement? ScheduledCrossPostConfig { get; init; }

    /// <summary>True when the viewer has muted this message's author.</summary>
    public bool ViewerHasMuted { get; init; }

    /// <summary>InterlinedList reply count (flat).</summary>
    public int ReplyCount { get; init; }

    /// <summary>
    /// Relation counts, e.g. <c>{ "replies": 2 }</c>. Needs the explicit name —
    /// the leading underscore doesn't survive camelCase matching.
    /// </summary>
    [JsonPropertyName("_count")]
    public MessageCounts? Count { get; init; }

    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");
    public string AuthorDisplayName => User?.DisplayName ?? User?.Username ?? "unknown";
    public string AuthorHandle => User is null ? string.Empty : $"@{User.Username}";

    /// <summary>A Push or Quote of another message.</summary>
    public bool IsPushOrQuote => PushedMessageId is { Length: > 0 };

    /// <summary>
    /// A Quote adds commentary; a bare Push does not. The distinction is
    /// content-based — the wire type has one field for both.
    /// </summary>
    public bool IsQuote => IsPushOrQuote && Content.Trim().Length > 0;

    /// <summary>Replies on InterlinedList itself, preferring the relation count.</summary>
    public int LocalReplyCount => Count?.Replies ?? ReplyCount;
}

/// <summary>Relation counts the API returns under <c>_count</c>.</summary>
public sealed class MessageCounts
{
    public int Replies { get; init; }
}

/// <summary>The <c>linkMetadata</c> envelope: <c>{ "links": [ … ] }</c>.</summary>
public sealed class LinkMetadataEnvelope
{
    public List<LinkPreview>? Links { get; init; }

    /// <summary>The first successfully-fetched preview, or null.</summary>
    public LinkPreview? Primary =>
        Links?.FirstOrDefault(l => l.FetchStatus is null or "success" && l.Metadata is not null);
}

/// <summary>One unfurled link.</summary>
public sealed class LinkPreview
{
    public string? Url { get; init; }

    /// <summary>Source platform, e.g. <c>youtube</c>, <c>other</c>.</summary>
    public string? Platform { get; init; }

    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary><c>success</c> when the unfurl worked. Treat anything else as no preview.</summary>
    public string? FetchStatus { get; init; }

    public LinkPreviewMetadata? Metadata { get; init; }
}

/// <summary>The unfurled card itself.</summary>
public sealed class LinkPreviewMetadata
{
    /// <summary>e.g. <c>link</c>, <c>video</c>.</summary>
    public string? Type { get; init; }

    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Thumbnail { get; init; }

    /// <summary>OpenGraph type, when the source page declared one.</summary>
    public string? OgType { get; init; }
}

/// <summary>Where a message was syndicated.</summary>
public sealed class CrossPostUrl
{
    public string? Url { get; init; }

    /// <summary>e.g. <c>mastodon</c>, <c>bluesky</c>, <c>linkedin</c>.</summary>
    public string? Platform { get; init; }

    public string? StatusId { get; init; }

    /// <summary>Several ids when one post became a thread on that platform.</summary>
    public List<string>? StatusIds { get; init; }

    /// <summary>A display label for the platform, title-cased.</summary>
    public string PlatformLabel => Platform switch
    {
        null or "" => "Cross-posted",
        "linkedin" => "LinkedIn",
        "bluesky" => "Bluesky",
        "mastodon" => "Mastodon",
        "twitter" => "X (Twitter)",
        var p => char.ToUpperInvariant(p[0]) + p[1..],
    };
}

/// <summary>Reply count on one syndicated copy of a message.</summary>
public sealed class PlatformReplyCount
{
    public string? Platform { get; init; }
    public int Count { get; init; }

    /// <summary><c>success</c> when the count is trustworthy.</summary>
    public string? Status { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }
}
