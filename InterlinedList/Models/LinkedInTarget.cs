namespace InterlinedList.Models;

/// <summary>What <c>POST /api/linkedin/sync-pages</c> achieved.</summary>
public enum LinkedInSyncOutcome
{
    /// <summary>Pages were refreshed. Re-read targets to see them.</summary>
    Synced,

    /// <summary>
    /// LinkedIn isn't connected to this account
    /// (<c>400 code:"not_linked"</c>). The caller should offer the connect
    /// handoff — this is an expected state, not an error.
    /// </summary>
    NotLinked,
}

/// <summary>
/// A LinkedIn destination a post can be sent to, from
/// <c>GET /api/linkedin/targets</c>.
/// </summary>
/// <remarks>
/// The shape is <b>unverified against populated data</b> — the test account has
/// no LinkedIn identity, so the endpoint returns <c>{"targets":[]}</c>. Fields
/// here follow the <c>linkedInTargets</c> request contract documented on
/// <c>POST /api/messages</c> (<c>{kind: "personal"}</c> or
/// <c>{kind: "orgPage", pageId}</c>), and every member is optional so an
/// unexpected payload degrades rather than throwing. Tighten this once an
/// account with LinkedIn linked is available.
/// </remarks>
public sealed class LinkedInTarget
{
    /// <summary><c>personal</c> or <c>orgPage</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Set for an <c>orgPage</c> target.</summary>
    public string? PageId { get; init; }

    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }

    public bool IsOrgPage => string.Equals(Kind, "orgPage", StringComparison.OrdinalIgnoreCase);

    /// <summary>Best available label, falling back to the page id.</summary>
    public string Label =>
        DisplayName ?? Name ?? (IsOrgPage ? PageId ?? "Organization page" : "Personal profile");
}
