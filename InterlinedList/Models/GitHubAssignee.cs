using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// A GitHub account, as returned by GET /api/github/repos/{owner}/{repo}/assignees
/// and by the <c>user</c> / <c>assignee</c> / <c>assignees</c> fields of
/// <see cref="GitHubIssue"/> — GitHub's "simple user" shape, identical in all
/// four places (verified live 2026-09-15).
/// <para>
/// Like labels, the assignees endpoint is <b>capped at 30</b> and forwards no
/// paging parameters (microsoft/vscode returned exactly 30 with and without
/// <c>?per_page=100</c>, no <c>Link</c> header). An assignee picker on a large
/// repo shows only the first 30 candidates.
/// </para>
/// </summary>
public sealed class GitHubAssignee
{
    [JsonPropertyName("login")]
    public required string Login { get; init; }

    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    /// <summary>"User", "Bot", or "Organization".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("site_admin")]
    public bool SiteAdmin { get; init; }

    public string Handle => $"@{Login}";

    public bool IsBot => string.Equals(Type, "Bot", StringComparison.OrdinalIgnoreCase);
}
