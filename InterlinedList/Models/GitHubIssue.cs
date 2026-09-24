using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// An issue from GET /api/github/issues?repo=owner/repo — the raw upstream
/// GitHub issue object, forwarded unmodified (verified live 2026-09-15: 13 items
/// from Adron/dashingarrivals at <c>state=all</c>, 30 from octocat/Hello-World).
/// Only the fields the app can use are modelled; GitHub sends ~32 top-level keys
/// and the rest (events_url, timeline_url, reactions, sub_issues_summary,
/// performed_via_github_app, …) are deliberately dropped.
/// <para>
/// <b>Two traps recorded from the live probe.</b>
/// </para>
/// <para>
/// 1. <b>Pull requests come back as issues.</b> GitHub's issues API includes PRs,
/// and the proxy does not filter them: Adron/dashingarrivals at <c>state=all</c>
/// returned 13 items of which <b>12 were pull requests</b> — only #13 was a real
/// issue. Anything that maps rows to issues must filter on
/// <see cref="IsPullRequest"/> first.
/// </para>
/// <para>
/// 2. <b>The endpoint is not paginated.</b> It returns GitHub's first page only —
/// exactly 30 items for octocat/Hello-World — and forwards neither <c>page</c>
/// nor <c>per_page</c> (both were passed live and changed nothing: same 30
/// issues, same order, no <c>Link</c> header). Unlike
/// <c>GET /api/github/repos</c>, which the server pages through completely,
/// there is no way to reach issue 31+ through this endpoint.
/// </para>
/// </summary>
public sealed class GitHubIssue
{
    /// <summary>GitHub's global issue id — 64-bit (observed 5414289061, which overflows int).</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    /// <summary>The per-repository issue number — what <c>#13</c> means and what the PATCH route is keyed by.</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Markdown body. Null for issues created with no description.</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>"open" or "closed".</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>"completed", "not_planned", "reopened", or null (null throughout the live sample).</summary>
    [JsonPropertyName("state_reason")]
    public string? StateReason { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }

    /// <summary>Comment count, not the comments themselves.</summary>
    [JsonPropertyName("comments")]
    public int Comments { get; init; }

    /// <summary>"OWNER", "MEMBER", "CONTRIBUTOR", "NONE", …</summary>
    [JsonPropertyName("author_association")]
    public string? AuthorAssociation { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("closed_at")]
    public DateTimeOffset? ClosedAt { get; init; }

    [JsonPropertyName("user")]
    public GitHubAssignee? User { get; init; }

    /// <summary>GitHub's legacy single-assignee field; <see cref="Assignees"/> is the real list.</summary>
    [JsonPropertyName("assignee")]
    public GitHubAssignee? Assignee { get; init; }

    [JsonPropertyName("assignees")]
    public List<GitHubAssignee>? Assignees { get; init; }

    /// <summary>
    /// Labels as objects — what the live sample always contained. GitHub's own
    /// schema technically permits bare strings here for some payloads; if a
    /// deserialization error ever surfaces on this field, that is why.
    /// </summary>
    [JsonPropertyName("labels")]
    public List<GitHubLabel>? Labels { get; init; }

    /// <summary>Present only on pull requests — the marker that separates PRs from issues.</summary>
    [JsonPropertyName("pull_request")]
    public GitHubPullRequestRef? PullRequest { get; init; }

    /// <summary>True when this "issue" is really a pull request. See the type remarks.</summary>
    public bool IsPullRequest => PullRequest is not null;

    public bool IsOpen => string.Equals(State, "open", StringComparison.OrdinalIgnoreCase);

    public string NumberLabel => $"#{Number}";

    public IReadOnlyList<GitHubLabel> LabelsOrEmpty => Labels ?? (IReadOnlyList<GitHubLabel>)Array.Empty<GitHubLabel>();

    public IReadOnlyList<GitHubAssignee> AssigneesOrEmpty => Assignees ?? (IReadOnlyList<GitHubAssignee>)Array.Empty<GitHubAssignee>();

    public string LabelNames => string.Join(", ", LabelsOrEmpty.Select(l => l.Name));

    public string AssigneeLogins => string.Join(", ", AssigneesOrEmpty.Select(a => a.Login));

    public string AuthorHandle => User is null ? string.Empty : User.Handle;
}

/// <summary>
/// The <c>pull_request</c> sub-object GitHub attaches to issues that are
/// actually PRs (verified live 2026-09-15). Modelled only so its presence can be
/// detected and linked; the app does not manage pull requests.
/// </summary>
public sealed class GitHubPullRequestRef
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("diff_url")]
    public string? DiffUrl { get; init; }

    [JsonPropertyName("patch_url")]
    public string? PatchUrl { get; init; }

    [JsonPropertyName("merged_at")]
    public DateTimeOffset? MergedAt { get; init; }
}
