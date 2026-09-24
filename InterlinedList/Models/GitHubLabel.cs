using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// A repository label, from GET /api/github/repos/{owner}/{repo}/labels and from
/// the <c>labels</c> array on <see cref="GitHubIssue"/> — the two are the same
/// shape (verified live 2026-09-15 against Adron/dashingarrivals, 10 labels, and
/// octocat/Hello-World, 1 label).
/// <para>
/// The labels endpoint is <b>capped at GitHub's default page size of 30</b> and
/// forwards no paging parameters: microsoft/vscode, which has far more, returned
/// exactly 30 both bare and with <c>?per_page=100</c>, with identical contents
/// and no <c>Link</c> header. A label picker for a big repo will therefore be
/// incomplete — that is a server-side limitation, not a bug here.
/// </para>
/// </summary>
public sealed class GitHubLabel
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Six hex digits with no leading '#', e.g. <c>d73a4a</c> — GitHub's own format.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>True for the labels GitHub creates with every new repository (bug, documentation, …).</summary>
    [JsonPropertyName("default")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// <see cref="Color"/> normalised to <c>#rrggbb</c>, or null when GitHub sent
    /// none. Null rather than a made-up default on purpose: this is GitHub's data,
    /// and a fallback swatch is the view's decision to make from Strata tokens.
    /// </summary>
    public string? HexColor =>
        Color is not { Length: > 0 } c ? null
        : c.StartsWith('#') ? c
        : $"#{c}";
}
