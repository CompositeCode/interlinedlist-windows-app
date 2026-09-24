using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// One notification, from <c>GET /api/notifications</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reconciled against the live payload 2026-09-16 (37 items, union of keys).
/// Real keys: <c>id, title, body, actionUrl, type, metadata, createdAt, readAt,
/// routePath, target</c>.
/// </para>
/// <para>
/// <b>The OpenAPI schema for this endpoint is wrong</b> — its
/// <c>UserNotification</c> declares <c>read</c> and <c>userId</c> and its
/// example wraps items under <c>notifications</c>. The live response uses
/// <c>readAt</c>, has no <c>userId</c>, and the envelope key is <c>items</c>.
/// Don't "correct" this model toward the spec.
/// </para>
/// </remarks>
public sealed class NotificationItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Web action path, e.g. <c>/integrations</c>. Prefer <see cref="Target"/> for navigation.</summary>
    public string? ActionUrl { get; init; }

    /// <summary>
    /// Event kind. Observed live: <c>message_dig</c>, <c>message_push_plain</c>,
    /// <c>message_push_commentary</c>, <c>message_mention</c>,
    /// <c>integration_reconnect</c>.
    /// </summary>
    public string? Type { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>
    /// The server's own in-app route, e.g. <c>/message/{id}/thread</c>. This is a
    /// <b>web</b> route — parsing it would couple this app to the website's URL
    /// scheme, so <see cref="Target"/> is the preferred navigation source.
    /// Useful as a fallback for types whose destination isn't an object.
    /// </summary>
    public string? RoutePath { get; init; }

    /// <summary>
    /// Structured destination: <c>{messageId, listId, orgId}</c>. Switch on
    /// whichever field is non-null. Note there is <b>no user/profile slot</b>, so
    /// a follow notification's destination may not be expressible here — the
    /// test account had no <c>follow</c> notifications, so that case is
    /// unverified.
    /// </summary>
    public NotificationTarget? Target { get; init; }

    /// <summary>
    /// Nested event detail, e.g. <c>{type, eventAt, actorUserId, …}</c>. Kept as
    /// raw JSON: the shape varies by notification type and only a subset was
    /// observed, so typing it strictly would be guessing.
    /// </summary>
    public JsonElement? Metadata { get; init; }

    public bool IsUnread => ReadAt is null;
    public string TimeFormatted => CreatedAt.ToUniversalTime().ToString("HH:mm:ss'Z'");

    /// <summary>Where clicking this should go, derived from <see cref="Target"/>.</summary>
    public NotificationDestination Destination
    {
        get
        {
            if (Target?.MessageId is { Length: > 0 }) return NotificationDestination.Message;
            if (Target?.ListId is { Length: > 0 }) return NotificationDestination.List;
            if (Target?.OrgId is { Length: > 0 }) return NotificationDestination.Organization;

            // No object target. A few types still have a known home.
            return Type switch
            {
                "integration_reconnect" => NotificationDestination.ConnectedAccounts,
                _ => NotificationDestination.None,
            };
        }
    }

    /// <summary>True when clicking will actually go somewhere.</summary>
    public bool IsNavigable => Destination != NotificationDestination.None;
}

/// <summary>The structured destination on a notification.</summary>
public sealed class NotificationTarget
{
    public string? MessageId { get; init; }
    public string? ListId { get; init; }
    public string? OrgId { get; init; }

    public bool IsEmpty =>
        MessageId is null or "" && ListId is null or "" && OrgId is null or "";
}

/// <summary>Which view a notification click should open.</summary>
public enum NotificationDestination
{
    /// <summary>Unrecognized — the item is inert rather than throwing.</summary>
    None,
    Message,
    List,
    Organization,
    ConnectedAccounts,
}
