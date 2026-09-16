using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The GitHub-backing facts about one list — <c>source</c>, <c>githubRepo</c>
/// and <c>githubRepoPrivate</c> — projected out of the raw list JSON.
///
/// <para>
/// <b>Why this exists instead of properties on <see cref="ListSummary"/>.</b>
/// The API does return those three fields on every list (verified live
/// 2026-09-16: <c>GET /api/lists</c> gives
/// <c>"source": "local", "githubRepo": null, "githubRepoPrivate": null</c> for
/// the test account's one list), but <see cref="ListSummary"/> is being extended
/// to the full wire type in a separate open PR. Reading the three fields out of
/// the raw JSON here keeps this feature independent of that change and merges
/// cleanly either way; once <see cref="ListSummary"/> carries them,
/// <c>InterlinedApiClient.GetListGitHubBackingAsync</c> is the only thing that
/// needs to change.
/// </para>
///
/// <para>
/// <b><see cref="RepoPrivate"/> is deliberately <c>bool?</c>.</b> /help/lists:
/// "Visibility is re-read from GitHub each time the list syncs… Lists created
/// before this tag existed show no tag until their first sync." A non-nullable
/// <c>bool</c> would default to <c>false</c> and silently assert *public repo*
/// for a list whose visibility has never been recorded. Since the tag exists to
/// warn that GitHub will show a collaborator a sign-in page or a 404, asserting
/// the wrong direction is a real (if small) trust problem — so
/// <see cref="ShowPrivateRepoTag"/> fires only on an explicit <c>true</c>, and
/// <see cref="VisibilityUnrecorded"/> names the third state rather than folding
/// it into "public".
/// </para>
///
/// <para>
/// The private/public flag is about <b>the repository on GitHub</b>, not about
/// who can see the InterlinedList list — <see cref="ListSummary.IsPublic"/> is
/// that, and the two are set separately.
/// </para>
/// </summary>
public sealed class GitHubListBacking
{
    /// <summary>The InterlinedList list id these facts belong to.</summary>
    public required string ListId { get; init; }

    /// <summary>The list's title, carried along so a badge or header can label itself.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// <c>source</c> — <c>"local"</c> or <c>"github"</c> (both observed live;
    /// <c>"github"</c> is what <c>POST /api/lists</c> requires to build a
    /// GitHub-backed list).
    /// </summary>
    public string Source { get; init; } = LocalSource;

    /// <summary><c>githubRepo</c> — <c>owner/repo</c>, or null on a local list.</summary>
    public string? Repo { get; init; }

    /// <summary>
    /// <c>githubRepoPrivate</c>. <c>true</c> = private repository on GitHub,
    /// <c>false</c> = public, <c>null</c> = never recorded (no sync yet) — see the
    /// class remarks for why the null case must not render as "public".
    /// </summary>
    public bool? RepoPrivate { get; init; }

    public const string LocalSource = "local";
    public const string GitHubSource = "github";

    /// <summary>
    /// Whether this list syncs its rows from GitHub issues. Keyed off
    /// <c>source</c>, with a populated <c>githubRepo</c> as a fallback so a list
    /// whose <c>source</c> string ever changes spelling still reads correctly.
    /// </summary>
    public bool IsGitHubBacked =>
        string.Equals(Source, GitHubSource, StringComparison.OrdinalIgnoreCase) ||
        Repo is { Length: > 0 };

    /// <summary>
    /// Show the "Private repo" tag — <b>only</b> on an explicit <c>true</c>.
    /// </summary>
    public bool ShowPrivateRepoTag => IsGitHubBacked && RepoPrivate == true;

    /// <summary>
    /// GitHub-backed, but visibility has never been recorded: show no tag at all
    /// and say so, rather than implying the repository is public.
    /// </summary>
    public bool VisibilityUnrecorded => IsGitHubBacked && RepoPrivate is null;

    /// <summary>Owner login half of <c>owner/repo</c>.</summary>
    public string Owner =>
        Repo is { Length: > 0 } r && r.IndexOf('/') is var i && i > 0 ? r[..i] : string.Empty;

    /// <summary>Repository-name half of <c>owner/repo</c> — the default list title.</summary>
    public string RepoName =>
        Repo is { Length: > 0 } r && r.IndexOf('/') is var i && i >= 0 && i < r.Length - 1
            ? r[(i + 1)..]
            : Repo ?? string.Empty;

    /// <summary>The link label the web app shows under the list name: "owner/repo issues".</summary>
    public string RepoLinkLabel => Repo is { Length: > 0 } r ? $"{r} issues" : string.Empty;

    /// <summary>The repository's issues page, opened in the OS browser.</summary>
    public string? IssuesUrl => Repo is { Length: > 0 } r ? $"https://github.com/{r}/issues" : null;

    /// <summary>
    /// Reads the three backing fields out of one list object — the element under
    /// <c>data</c> for <c>GET /api/lists/{id}</c>, or one entry of the
    /// <c>lists</c> array for <c>GET /api/lists</c>. Missing fields degrade to a
    /// local list rather than throwing, because this has to survive both the
    /// current and the extended <see cref="ListSummary"/> wire shape.
    /// </summary>
    public static GitHubListBacking FromListJson(JsonElement list, string? fallbackId = null)
    {
        string? Str(string name) =>
            list.ValueKind == JsonValueKind.Object &&
            list.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        bool? Bool(string name)
        {
            if (list.ValueKind != JsonValueKind.Object || !list.TryGetProperty(name, out var p))
                return null;
            return p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return new GitHubListBacking
        {
            ListId = Str("id") ?? fallbackId ?? string.Empty,
            Title = Str("title"),
            Source = Str("source") ?? LocalSource,
            Repo = Str("githubRepo"),
            RepoPrivate = Bool("githubRepoPrivate")
        };
    }

    /// <summary>A list we know nothing GitHub-ish about — renders no badge, no tag.</summary>
    public static GitHubListBacking Local(string listId) => new() { ListId = listId };
}
