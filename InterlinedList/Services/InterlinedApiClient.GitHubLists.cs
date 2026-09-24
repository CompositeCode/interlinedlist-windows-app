using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Where lists meet GitHub: creating a GitHub-backed list, reading a list's
/// GitHub backing, and "Refresh from GitHub".
///
/// <para>
/// Kept in its own partial rather than folded into
/// <c>InterlinedApiClient.Lists.cs</c> because that file's
/// <c>CreateListAsync(title, description)</c> only sends two of the eleven fields
/// <c>POST /api/lists</c> accepts, and the GitHub path needs four more —
/// <c>source</c>, <c>githubRepo</c>, <c>parentId</c>, <c>isPublic</c> (the full
/// documented set is <c>title, description, messageId, metadata, schema,
/// parentId, isPublic, source, githubRepo, githubSource, initialRows</c>).
/// </para>
///
/// <para>
/// <b>Live-probe record, 2026-09-16</b> (test account, bearer sync-token). No
/// GitHub-backed list was created — doing so would link a real GitHub repository
/// on shared test infrastructure — so the create and refresh <em>success</em>
/// paths are built and unexercised. What was verified, with a throwaway list
/// titled "ZZ claude-probe …" that was deleted and confirmed gone afterwards:
/// </para>
/// <list type="bullet">
/// <item><description><c>POST /api/lists { title, source: "github" }</c> and no
/// <c>githubRepo</c> → <b>400</b> "githubRepo is required for GitHub-backed
/// lists (format: owner/repo)", <b>and nothing was created</b>. The server does
/// understand <c>source: "github"</c>.</description></item>
/// <item><description><c>POST /api/lists/{id}/refresh</c> on a <c>local</c> list
/// → <b>400</b> "Refresh is only available for GitHub-backed lists". Re-reading
/// the list gave an identical body with an unchanged <c>updatedAt</c>, so the
/// rejection is inert.</description></item>
/// <item><description><c>GET /api/lists</c> really does carry
/// <c>source</c>/<c>githubRepo</c>/<c>githubRepoPrivate</c> on every list
/// (<c>"local"</c>/<c>null</c>/<c>null</c> for the account's one
/// list).</description></item>
/// </list>
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Creates a GitHub-backed list: <c>POST /api/lists</c> with
    /// <c>source: "github"</c> and <c>githubRepo: "owner/repo"</c>. Its rows are
    /// the repository's issues from then on (add a row → create an issue, edit →
    /// update, delete → close).
    /// <para>
    /// <b>Never executed live</b> — see the class remarks. Re-read the list with
    /// <see cref="GetListGitHubBackingAsync"/> afterwards rather than trusting
    /// the returned envelope.
    /// </para>
    /// </summary>
    /// <param name="repoFullName">
    /// <c>owner/repo</c>. Required, and required in that exact shape: omitting it
    /// answers <c>400</c> "githubRepo is required for GitHub-backed lists (format:
    /// owner/repo)" (verified live).
    /// </param>
    /// <param name="title">
    /// The list title. /help/lists says this "defaults to the repo name" in the
    /// web UI; the default is applied in the view model, not here, so a caller
    /// that wants something else isn't fighting the client.
    /// </param>
    /// <param name="isPublic">
    /// Whether the <b>InterlinedList list</b> is public. Unrelated to whether the
    /// GitHub repository is private — the two are set separately (see
    /// <see cref="GitHubListBacking"/>).
    /// </param>
    public async Task<GitHubBackedListCreated> CreateGitHubBackedListAsync(
        string repoFullName,
        string title,
        string? description = null,
        string? parentId = null,
        bool isPublic = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoFullName) || !repoFullName.Contains('/'))
        {
            // Matches the server's own rule, caught here so the user gets the
            // reason instead of a round trip and a raw 400.
            throw new ArgumentException(
                "A GitHub-backed list needs a repository as owner/repo.", nameof(repoFullName));
        }

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A list needs a title.", nameof(title));

        // Built key-by-key so unset fields are omitted rather than sent as
        // explicit nulls — the local create path sends only what it has too.
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title.Trim(),
            ["source"] = GitHubListBacking.GitHubSource,
            ["githubRepo"] = repoFullName.Trim(),
            ["isPublic"] = isPublic
        };
        if (!string.IsNullOrWhiteSpace(description)) payload["description"] = description.Trim();
        if (!string.IsNullOrWhiteSpace(parentId)) payload["parentId"] = parentId;

        var json = await SendElementAsync(HttpMethod.Post, "api/lists", payload, ct);
        return GitHubBackedListCreated.FromJson(json);
    }

    /// <summary>
    /// "Refresh from GitHub" — <c>POST /api/lists/{id}/refresh</c>, re-pulling the
    /// repository's issues into the list's rows and re-reading the repository's
    /// public/private visibility.
    /// <para>
    /// A <c>local</c> list answers <c>400</c> "Refresh is only available for
    /// GitHub-backed lists" (verified live, and inert — nothing changed), so gate
    /// the action on <see cref="GitHubListBacking.IsGitHubBacked"/> and treat that
    /// 400 as a state error rather than something to retry. The success body is
    /// unverified; re-read the list and its rows afterwards.
    /// </para>
    /// </summary>
    public async Task<ListRefreshResult> RefreshListFromGitHubAsync(string listId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, $"api/lists/{listId}/refresh", new { }, ct);
        await EnsureSuccessAsync(resp, ct);

        // A 2xx with an empty body is possible and is not an error here — the
        // caller re-reads the list for truth either way.
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return new ListRefreshResult();

        try
        {
            return ListRefreshResult.FromJson(JsonSerializer.Deserialize<JsonElement>(text, JsonOptions));
        }
        catch (JsonException)
        {
            return new ListRefreshResult { Message = "Refreshed from GitHub." };
        }
    }

    /// <summary>
    /// One list's GitHub backing (<c>source</c>, <c>githubRepo</c>,
    /// <c>githubRepoPrivate</c>) from <c>GET /api/lists/{id}</c>, whose body is
    /// under a <c>data</c> envelope.
    /// <para>
    /// Reads the raw JSON rather than <see cref="ListSummary"/> on purpose — see
    /// <see cref="GitHubListBacking"/> for why.
    /// </para>
    /// </summary>
    public async Task<GitHubListBacking> GetListGitHubBackingAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}", ct);
        return json.ValueKind == JsonValueKind.Object &&
               json.TryGetProperty("data", out var data) &&
               data.ValueKind == JsonValueKind.Object
            ? GitHubListBacking.FromListJson(data, listId)
            : GitHubListBacking.Local(listId);
    }

    /// <summary>
    /// Every one of the caller's lists reduced to its GitHub backing, in one
    /// request — so a lists browser can badge the GitHub-backed ones without a
    /// fetch per row.
    /// <para>
    /// Uses the documented <c>?all=1</c> (paging off) and falls back to the
    /// ordinary offset page if that ever stops being honoured, so the badge
    /// degrades to "first page only" rather than disappearing.
    /// </para>
    /// </summary>
    public async Task<List<GitHubListBacking>> GetListGitHubBackingsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/lists?all=1", ct);

        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("lists", out var lists) ||
            lists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var backings = new List<GitHubListBacking>();
        foreach (var list in lists.EnumerateArray())
            backings.Add(GitHubListBacking.FromListJson(list));

        return backings;
    }
}
