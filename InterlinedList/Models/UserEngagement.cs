namespace InterlinedList.Models;

/// <summary>
/// Aggregate engagement the current user's own messages have received, from
/// <c>GET /api/user/engagement</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This endpoint was documented in <c>CLAUDE.md</c> as returning <c>401</c>
/// with a bearer token and therefore out of scope.</b> That was true on
/// 2026-07-31 and is not true now — re-probed 2026-09-16 it returns <c>200</c>:
/// </para>
/// <code>
/// {"totalDigs":24,"totalPushes":6,
///  "recent":[{"id","type","title","body","routePath","sourceMessageId","createdAt"}, …]}
/// </code>
/// <para>
/// Ten <c>recent</c> rows were returned. Note the rows carry <c>routePath</c>
/// and <c>sourceMessageId</c>, so each one is directly navigable.
/// </para>
/// </remarks>
public sealed class UserEngagement
{
    /// <summary>Total Digs received across the user's own messages.</summary>
    public int TotalDigs { get; init; }

    /// <summary>Total Pushes (reposts) received.</summary>
    public int TotalPushes { get; init; }

    /// <summary>Most recent engagement events, newest first.</summary>
    public List<EngagementEvent>? Recent { get; init; }

    public int TotalEngagement => TotalDigs + TotalPushes;
    public bool HasAny => TotalEngagement > 0;
}

/// <summary>One engagement event on one of the user's messages.</summary>
public sealed class EngagementEvent
{
    public required string Id { get; init; }

    /// <summary>e.g. <c>message_dig</c>, <c>message_push_plain</c>.</summary>
    public string? Type { get; init; }

    public string? Title { get; init; }
    public string? Body { get; init; }

    /// <summary>Server-provided in-app route, e.g. <c>/message/{id}/thread</c>.</summary>
    public string? RoutePath { get; init; }

    /// <summary>The message that was engaged with — enough to navigate without parsing <see cref="RoutePath"/>.</summary>
    public string? SourceMessageId { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// A one-line summary. The server's <c>body</c> quotes the whole message, so
    /// it needs trimming before it goes in a narrow rail.
    /// </summary>
    public string Summary => Title ?? Body ?? Type ?? "Engagement";
}
