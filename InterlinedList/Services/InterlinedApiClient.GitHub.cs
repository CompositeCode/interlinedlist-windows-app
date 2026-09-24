using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The nine <c>/api/github/*</c> endpoints — thin server-side proxies over the
/// GitHub REST API, acting as the user's linked GitHub identity. They power
/// GitHub-backed lists, where rows map to issues: adding a row creates an issue,
/// editing a row updates it, deleting a row closes it (see /help/lists).
///
/// <para>
/// <b>Status correction, probed live 2026-09-15 with the test account.</b>
/// CLAUDE.md records that "every <c>/api/github/*</c> call returns 'GitHub
/// account not linked'". That is stale, and so is the follow-on assumption that
/// the test account has no GitHub identity: <c>GET /api/user/identities</c>
/// lists <c>provider: "github"</c> as <c>InterlinedListMessenger</c> (connected
/// 2026-08-22, last verified 2026-09-15). Every endpoint answers the bearer
/// sync-token, and the ones that come back empty are empty because that
/// particular GitHub account owns nothing — not because of an auth wall. Full
/// probe results per endpoint:
/// </para>
/// <list type="bullet">
/// <item><description><c>GET /repos</c> → <b>200</b> bare array. <c>[]</c> bare,
/// but <c>?org=github</c> → <b>559</b> items, <c>?org=dotnet</c> → 278,
/// <c>?org=microsoft</c> → 2000.</description></item>
/// <item><description><c>GET /orgs</c> → <b>200 []</b> (linked account is in no
/// organizations; item shape never observed).</description></item>
/// <item><description><c>GET /issues</c> → <b>400</b> <c>bad_request</c> with no
/// <c>repo</c> and no user default; <b>200</b> array with
/// <c>?repo=owner/repo</c>; <b>404</b> for a repo the token cannot reach;
/// <b>422</b> <c>validation_failed</c> for an unknown <c>state</c>.</description></item>
/// <item><description><c>GET /repos/{o}/{r}/assignees</c> → <b>200</b> array (30 max).</description></item>
/// <item><description><c>GET /repos/{o}/{r}/labels</c> → <b>200</b> array (30 max).</description></item>
/// <item><description><c>GET /repos/{o}/{r}/next-issue-number</c> → <b>200</b>
/// <c>{ "nextNumber": 14 }</c>; <c>1</c> for a repo with no issues.</description></item>
/// <item><description>The three write endpoints were <b>deliberately not
/// called</b> — they create real issues and comments in real GitHub
/// repositories. Their responses stay loosely typed
/// (<see cref="JsonElement"/>) per this repo's read-after-write rule.</description></item>
/// </list>
///
/// <para>
/// <b>Pagination — the one thing to get right.</b> Only <c>GET /repos</c> is
/// paginated, and it is paginated <em>server-side, completely, in a single
/// response</em>: a bare JSON array with no envelope, no <c>Link</c> header, and
/// no <c>page</c>/<c>per_page</c> parameters (none are documented in the OpenAPI
/// spec, and passing them changed nothing live). 559 repos for <c>?org=github</c>
/// and 278 for <c>?org=dotnet</c> arrived in one call, well past GitHub's own
/// 100-per-page ceiling, which is what the docs' "fully paginated across all
/// affiliations" means. Note a probable server-side safety cap of <b>2000</b>:
/// <c>?org=microsoft</c>, <c>?org=google</c> and <c>?org=apache</c> each returned
/// exactly 2000 while <c>?org=aws</c> returned a natural 552. Issues, labels and
/// assignees are the opposite case — each capped at GitHub's default 30 with no
/// way to page further (see the remarks on <see cref="GitHubIssue"/>).
/// </para>
///
/// <para>
/// <b>Link state.</b> Because these endpoints return empty collections rather
/// than errors, "empty" never means "not linked" — use
/// <see cref="GetGitHubLinkStateAsync"/>, which reads
/// <c>GET /api/user/identities</c>, for the unambiguous answer, and
/// <see cref="ClassifyGitHubFailure"/> to turn a failure into a specific reason.
/// </para>
/// </summary>
public sealed partial class InterlinedApiClient
{
    // ── Reads (all live-probed 2026-09-15) ──────────────────────────────────────

    /// <summary>
    /// Repositories the linked GitHub account can reach, optionally restricted to
    /// one organization. The complete set arrives in one response — see the
    /// pagination remarks on the class.
    /// <para>
    /// <c>org</c> must be an actual <b>organization</b> login. A user login 404s
    /// (<c>?org=Adron</c> → <c>404 not_found</c>, because GitHub's
    /// <c>/orgs/{org}/repos</c> has no such org), as does a nonexistent one. The
    /// user's own repositories come from the unfiltered call instead. Any public
    /// org works, not just ones the account belongs to — <c>?org=github</c>
    /// returned 559 repos from an account that is a member of no organizations.
    /// </para>
    /// </summary>
    public async Task<List<GitHubRepo>> GetGitHubReposAsync(string? org = null, CancellationToken ct = default)
    {
        const string basePath = "api/github/repos";
        var query = string.IsNullOrWhiteSpace(org) ? string.Empty : $"?org={Uri.EscapeDataString(org)}";

        var repos = new List<GitHubRepo>();

        // The live shape is a bare, already-complete array, so this normally runs
        // exactly once. The loop exists so that if the endpoint ever moves to the
        // paginated envelope the rest of this API uses ({ items, pagination }),
        // the picker keeps seeing the whole set instead of silently truncating to
        // the first page. Hard-capped so a misread envelope can't spin forever.
        for (var page = 1; page <= 50; page++)
        {
            var path = page == 1
                ? basePath + query
                : $"{basePath}{query}{(query.Length == 0 ? "?" : "&")}page={page}";

            var json = await GetElementAsync(path, ct);

            if (json.ValueKind == JsonValueKind.Array)
            {
                // Live shape: the server already paged through GitHub for us.
                repos.AddRange(json.Deserialize<List<GitHubRepo>>(JsonOptions) ?? []);
                break;
            }

            if (json.ValueKind != JsonValueKind.Object)
                break;

            var items = FirstArrayProperty(json, "repos", "repositories", "items", "data");
            if (items is null)
                break;

            repos.AddRange(items.Value.Deserialize<List<GitHubRepo>>(JsonOptions) ?? []);

            if (!HasMorePages(json))
                break;
        }

        return repos;
    }

    /// <summary>
    /// Organizations the linked GitHub account belongs to, so a repo picker can
    /// group by org and feed <c>org</c> to <see cref="GetGitHubReposAsync"/>.
    /// <para>
    /// Returned <c>200 []</c> on the test account, so no item was ever seen. This
    /// accepts both an array of objects and an array of bare login strings
    /// precisely because the shape is unconfirmed — see <see cref="GitHubOrg"/>.
    /// </para>
    /// </summary>
    public async Task<List<GitHubOrg>> GetGitHubOrgsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/github/orgs", ct);

        var array = json.ValueKind == JsonValueKind.Array
            ? json
            : FirstArrayProperty(json, "orgs", "organizations", "items", "data");

        if (array is null)
            return [];

        var orgs = new List<GitHubOrg>();
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
                orgs.Add(new GitHubOrg { Login = element.GetString() });
            else if (element.ValueKind == JsonValueKind.Object &&
                     element.Deserialize<GitHubOrg>(JsonOptions) is { } org)
                orgs.Add(org);
        }

        return orgs;
    }

    /// <summary>
    /// Issues for one repository. The response is GitHub's raw issue array,
    /// forwarded unmodified — <b>including pull requests</b>, and <b>capped at
    /// GitHub's first 30</b>. Filter on <see cref="GitHubIssue.IsPullRequest"/>;
    /// see the remarks on <see cref="GitHubIssue"/> for the evidence.
    /// </summary>
    /// <param name="repo">
    /// <c>owner/repo</c>. When null the server falls back to the user's
    /// <c>githubDefaultRepo</c> (<see cref="GitHubLinkState.DefaultRepo"/>) and,
    /// if that is unset too, answers <c>400</c> "Repository required. Set default
    /// repo in Settings or pass repo parameter (owner/repo)." — classified as
    /// <see cref="GitHubFailureReason.RepositoryRequired"/>. A value with no
    /// slash gets the same 400.
    /// </param>
    /// <param name="state">
    /// <c>open</c> (server default), <c>closed</c>, or <c>all</c>. Lower-cased
    /// here because the server is case-sensitive: <c>state=OPEN</c> answers
    /// <c>422 validation_failed</c>.
    /// </param>
    public async Task<List<GitHubIssue>> GetGitHubIssuesAsync(
        string? repo = null, string? state = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(repo))
            query.Add($"repo={Uri.EscapeDataString(repo)}");
        if (!string.IsNullOrWhiteSpace(state))
            query.Add($"state={Uri.EscapeDataString(state.ToLowerInvariant())}");

        var path = "api/github/issues" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
        return await GetJsonAsync<List<GitHubIssue>>(path, ct);
    }

    /// <summary>
    /// Candidate assignees for a repository (GitHub's "simple user" objects).
    /// Capped at 30 — see <see cref="GitHubAssignee"/>. A repository the token
    /// cannot reach answers <c>404 not_found</c>.
    /// </summary>
    public Task<List<GitHubAssignee>> GetGitHubAssigneesAsync(string owner, string repo, CancellationToken ct = default)
        => GetJsonAsync<List<GitHubAssignee>>(
            $"api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/assignees", ct);

    /// <summary>
    /// Labels defined on a repository. Capped at 30 — see
    /// <see cref="GitHubLabel"/>. A repository the token cannot reach answers
    /// <c>404 not_found</c>.
    /// </summary>
    public Task<List<GitHubLabel>> GetGitHubLabelsAsync(string owner, string repo, CancellationToken ct = default)
        => GetJsonAsync<List<GitHubLabel>>(
            $"api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/labels", ct);

    /// <summary>
    /// The number a newly created issue will get — <c>max(issue number) + 1</c>,
    /// so a compose form can show "#14" before anything is written.
    /// <para>
    /// Response is <c>{ "nextNumber": 14 }</c> (verified live against
    /// Adron/dashingarrivals, whose highest issue is #13). A repository with no
    /// issues answers <c>{ "nextNumber": 1 }</c>, and an unreachable one
    /// <c>404 not_found</c>. Advisory only: the real number is whatever GitHub
    /// assigns, which can differ if anything else is filed in between.
    /// </para>
    /// </summary>
    public async Task<int> GetGitHubNextIssueNumberAsync(string owner, string repo, CancellationToken ct = default)
    {
        var json = await GetElementAsync(
            $"api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/next-issue-number", ct);

        return json.TryGetProperty("nextNumber", out var next) && next.TryGetInt32(out var value)
            ? value
            : throw new InterlinedApiException(200, "GET /api/github/repos/{owner}/{repo}/next-issue-number returned no nextNumber.");
    }

    // ── Writes (NOT live-verified — deliberately never called) ──────────────────
    // Calling these creates real issues and real comments in real GitHub
    // repositories, so unlike the reads above they were not exercised against the
    // test account. Each returns the raw JsonElement instead of a typed model:
    // /help/api/github-integration says they return the upstream GitHub issue /
    // comment object, which would deserialize into GitHubIssue, but that is
    // documentation rather than observation. Treat the return value as a hint,
    // re-fetch with GetGitHubIssuesAsync for truth, and type these strictly only
    // after a live write confirms the envelope.
    //
    // One documented discrepancy to keep in mind: the OpenAPI spec types `labels`
    // and `assignees` as `string`, while /help/api/github-integration shows them
    // as string ARRAYS (`"labels": ["bug"]`) and notes that non-string entries are
    // silently filtered. The help page is the more specific source, so arrays are
    // what these send; if a live write ever 400s, that is the first thing to flip.

    /// <summary>
    /// Creates an issue (<c>POST /api/github/issues</c>, documented 201). In the
    /// GitHub-backed-list model this is what "add a row" does.
    /// <b>Unverified response envelope</b> — see the section remarks.
    /// </summary>
    /// <param name="repo"><c>owner/repo</c>.</param>
    public Task<JsonElement> CreateGitHubIssueAsync(
        string repo,
        string title,
        string? body = null,
        IReadOnlyList<string>? labels = null,
        IReadOnlyList<string>? assignees = null,
        CancellationToken ct = default)
    {
        // Built key-by-key so absent fields are omitted rather than sent as
        // explicit nulls — these are forwarded to GitHub, which is stricter about
        // that than the InterlinedList API is.
        var payload = new Dictionary<string, object?>
        {
            ["repo"] = repo,
            ["title"] = title
        };
        if (body is not null) payload["body"] = body;
        if (labels is { Count: > 0 }) payload["labels"] = labels;
        if (assignees is { Count: > 0 }) payload["assignees"] = assignees;

        return SendElementAsync(HttpMethod.Post, "api/github/issues", payload, ct);
    }

    /// <summary>
    /// Updates an existing issue's labels and/or assignees
    /// (<c>PATCH /api/github/issues/{owner}/{repo}/{number}</c>). In the
    /// GitHub-backed-list model this is what "edit a row" does — note the route
    /// only accepts labels and assignees, so title/body/state are <b>not</b>
    /// editable through it. <b>Unverified response envelope.</b>
    /// <para>
    /// At least one of <paramref name="labels"/> or <paramref name="assignees"/>
    /// must be supplied; the server answers <c>400</c> otherwise
    /// (/help/api/github-integration), so this throws before making a pointless
    /// round trip. Pass an empty list to clear a field, which is distinct from
    /// passing null to leave it alone.
    /// </para>
    /// </summary>
    public Task<JsonElement> UpdateGitHubIssueAsync(
        string owner,
        string repo,
        int number,
        IReadOnlyList<string>? labels = null,
        IReadOnlyList<string>? assignees = null,
        CancellationToken ct = default)
    {
        if (labels is null && assignees is null)
            throw new ArgumentException("PATCH /api/github/issues requires labels and/or assignees.", nameof(labels));

        var payload = new Dictionary<string, object?>();
        if (labels is not null) payload["labels"] = labels;
        if (assignees is not null) payload["assignees"] = assignees;

        return SendElementAsync(
            HttpMethod.Patch,
            $"api/github/issues/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{number}",
            payload, ct);
    }

    /// <summary>
    /// Adds a comment to an issue
    /// (<c>POST /api/github/issues/{owner}/{repo}/{number}/comments</c>,
    /// documented 201). <c>body</c> must be a non-empty trimmed string or the
    /// server answers <c>400</c>. <b>Unverified response envelope.</b>
    /// </summary>
    public Task<JsonElement> AddGitHubIssueCommentAsync(
        string owner, string repo, int number, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("A GitHub issue comment body cannot be empty.", nameof(body));

        return SendElementAsync(
            HttpMethod.Post,
            $"api/github/issues/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{number}/comments",
            new { body = body.Trim() }, ct);
    }

    // ── Link state and the reconnect handoff ────────────────────────────────────

    /// <summary>
    /// The browser handoff for "Connect GitHub" / "Reconnect for GitHub Issues".
    /// The <c>link=true</c> parameter is what upgrades the requested scope from
    /// sign-in-only to Issues-capable — see
    /// <see cref="GitHubLinkState.ReconnectUrl"/> for the verified evidence.
    /// </summary>
    public static string GitHubReconnectUrl => $"{ApiConfig.BaseUrl}api/auth/github/authorize?link=true";

    /// <summary>
    /// Whether this user has a GitHub identity linked, plus everything the UI
    /// needs to offer the reconnect handoff. This is the <b>distinguishable
    /// state</b> to branch on before calling any of the reads above — an empty
    /// repo or org list does not mean "not linked".
    /// <para>
    /// Composed from three calls. <c>GET /api/user/identities</c> is the
    /// authoritative per-user signal and is required; the other two
    /// (<c>GET /api/auth/github/status</c> for the org-access deep link and
    /// <c>GET /api/user</c> for <c>githubDefaultRepo</c>) are enrichment and are
    /// swallowed on failure, so a hiccup there can't turn a known link state into
    /// an error dialog.
    /// </para>
    /// </summary>
    public async Task<GitHubLinkState> GetGitHubLinkStateAsync(CancellationToken ct = default)
    {
        var identities = await GetLinkedIdentitiesAsync(ct);

        // Providers can be instance-qualified ("mastodon:techhub.social"), so
        // match the provider family rather than requiring an exact string.
        var github = identities.FirstOrDefault(i =>
            i.Provider.Equals("github", StringComparison.OrdinalIgnoreCase) ||
            i.Provider.StartsWith("github:", StringComparison.OrdinalIgnoreCase));

        GitHubProviderStatus? status = null;
        try
        {
            status = await GetJsonAsync<GitHubProviderStatus>("api/auth/github/status", ct);
        }
        catch (Exception e) when (e is InterlinedApiException or HttpRequestException or JsonException)
        {
            // Enrichment only — the link answer above already stands. The catch is
            // deliberately wider than InterlinedApiException: CLAUDE.md records a
            // real "installs but won't run" crash caused by catching only that on a
            // path that can equally throw network or JSON errors. Cancellation is
            // left to propagate.
        }

        string? defaultRepo = null;
        try
        {
            // Read straight out of the raw /api/user JSON: CurrentUser doesn't
            // carry githubDefaultRepo yet and is being changed elsewhere, so it is
            // deliberately left untouched here.
            var user = await GetElementAsync("api/user", ct);
            if (user.TryGetProperty("user", out var userObj) &&
                userObj.TryGetProperty("githubDefaultRepo", out var repoProp) &&
                repoProp.ValueKind == JsonValueKind.String)
            {
                defaultRepo = repoProp.GetString();
            }
        }
        catch (Exception e) when (e is InterlinedApiException or HttpRequestException or JsonException)
        {
            // Same — a missing default repo is not an error state.
        }

        return new GitHubLinkState
        {
            IsLinked = github is not null,
            // Defaults to true when the status call didn't come back: the fallback
            // has to keep the reconnect affordance offered rather than hide it on
            // the strength of a failed enrichment read.
            ProviderConfigured = status?.Configured ?? true,
            Username = github?.ProviderUsername,
            ProfileUrl = github?.ProfileUrl,
            AvatarUrl = github?.AvatarUrl,
            ConnectedAt = github?.ConnectedAt,
            LastVerifiedAt = github?.LastVerifiedAt,
            DefaultRepo = defaultRepo,
            ManageOrgAccessUrl = status?.ManageOrgAccessUrl,
            ReconnectUrl = GitHubReconnectUrl
        };
    }

    /// <summary>
    /// Hands <see cref="GitHubReconnectUrl"/> to the OS default browser, the same
    /// way <c>OpenProviderAuthorize</c> does for the cross-post providers. Kept
    /// separate from it because the GitHub flow needs <c>?link=true</c> to
    /// request the Issues scope, which that method does not send.
    /// </summary>
    public static void OpenGitHubReconnect()
    {
        // UseShellExecute = true is required — without it, Process.Start on
        // .NET Core/5+ won't hand the URL to the OS's default browser.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = GitHubReconnectUrl,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Turns a failed GitHub call into a specific reason, so the UI can offer the
    /// right remedy instead of a generic error toast.
    /// <para>
    /// Everything except <see cref="GitHubFailureReason.NotLinked"/> and
    /// <see cref="GitHubFailureReason.RateLimited"/> was observed live on
    /// 2026-09-15 (see the class remarks). Those two could not be: the test
    /// account <em>is</em> linked, so the unlinked response was unobservable, and
    /// the rate limit was never hit. The unlinked signature below comes from
    /// CLAUDE.md's record of an older probe ("GitHub account not linked"), which
    /// is why <see cref="GetGitHubLinkStateAsync"/> — not this classifier — is the
    /// primary way to detect a missing link. Treat this as the backstop for a call
    /// that fails anyway (a revoked authorization, say).
    /// </para>
    /// </summary>
    public static GitHubFailureReason ClassifyGitHubFailure(InterlinedApiException ex)
    {
        var message = ex.Message ?? string.Empty;

        if (LooksUnlinked(message))
            return GitHubFailureReason.NotLinked;

        return ex.StatusCode switch
        {
            401 => GitHubFailureReason.NotAuthenticated,
            // Live: "Repository required. Set default repo in Settings or pass
            // repo parameter (owner/repo)." — also what a repo with no slash gets.
            400 when message.Contains("Repository required", StringComparison.OrdinalIgnoreCase)
                => GitHubFailureReason.RepositoryRequired,
            400 => GitHubFailureReason.InvalidRequest,
            // 403 is how GitHub reports a scope/permission refusal upstream, and
            // /help/lists documents github_repo_inaccessible as 403 *or* 404.
            403 => GitHubFailureReason.RepositoryInaccessible,
            // Live: missing repo, private repo the token can't see, and ?org=<user>
            // (a non-organization login) all return 404 not_found.
            404 => GitHubFailureReason.RepositoryInaccessible,
            422 => GitHubFailureReason.InvalidRequest,
            429 => GitHubFailureReason.RateLimited,
            _ => GitHubFailureReason.Unknown
        };
    }

    /// <summary>
    /// Convenience predicate for the "Reconnect for GitHub Issues" branch.
    /// Prefer <see cref="GetGitHubLinkStateAsync"/> when you can afford the call —
    /// it answers from <c>/api/user/identities</c> rather than from error text.
    /// </summary>
    public static bool IsGitHubNotLinked(InterlinedApiException ex)
        => ClassifyGitHubFailure(ex) == GitHubFailureReason.NotLinked;

    private static bool LooksUnlinked(string message)
        => message.Contains("github", StringComparison.OrdinalIgnoreCase) &&
           (message.Contains("not linked", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not connected", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no linked", StringComparison.OrdinalIgnoreCase));

    // ── Local plumbing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mutating call whose response body is returned untyped. Sits between the
    /// shared <c>SendVoidAsync</c> (discards the body) and
    /// <c>SendJsonAsync&lt;T&gt;</c> (needs a live-verified envelope) — which is
    /// exactly where the three unexercised GitHub writes belong.
    /// </summary>
    private async Task<JsonElement> SendElementAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    /// <summary>First of <paramref name="names"/> that is present and an array.</summary>
    private static JsonElement? FirstArrayProperty(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Whether a hypothetical paginated envelope says there is another page.
    /// Never true against the live bare-array shape — it exists so the forward
    /// compatibility loop in <see cref="GetGitHubReposAsync"/> has something
    /// honest to test.
    /// </summary>
    private static bool HasMorePages(JsonElement obj)
    {
        if (obj.TryGetProperty("pagination", out var pagination) &&
            pagination.ValueKind == JsonValueKind.Object &&
            pagination.TryGetProperty("hasMore", out var nested) &&
            nested.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return nested.GetBoolean();
        }

        return obj.TryGetProperty("hasMore", out var flat) &&
               flat.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               flat.GetBoolean();
    }
}

/// <summary>
/// Why a <c>/api/github/*</c> call failed, mapped from status code and message by
/// <see cref="InterlinedApiClient.ClassifyGitHubFailure"/>. The point of the enum
/// is that <see cref="NotLinked"/> and <see cref="RepositoryInaccessible"/> have
/// specific remedies — reconnect with the Issues scope, or grant organization
/// access on GitHub — that a generic error message would throw away.
/// </summary>
public enum GitHubFailureReason
{
    /// <summary>Unrecognised failure; show the server's message as-is.</summary>
    Unknown,

    /// <summary>
    /// <c>401 unauthorized</c> — the InterlinedList sync-token itself is missing
    /// or rejected. Nothing to do with GitHub; the session needs attention.
    /// </summary>
    NotAuthenticated,

    /// <summary>
    /// No usable GitHub identity on the account. The cue for the "Reconnect for
    /// GitHub Issues" handoff (<see cref="InterlinedApiClient.GitHubReconnectUrl"/>).
    /// </summary>
    NotLinked,

    /// <summary>
    /// <c>400</c> from <c>GET /api/github/issues</c> with no <c>repo</c> and no
    /// <c>githubDefaultRepo</c> set. Prompt for a repository, or set the default.
    /// </summary>
    RepositoryRequired,

    /// <summary>
    /// <c>403</c>/<c>404</c> — the repository doesn't exist, is private beyond the
    /// token's reach, or belongs to an organization that hasn't approved the
    /// OAuth app. The remedy is GitHub's authorized-apps page
    /// (<see cref="GitHubLinkState.ManageOrgAccessUrl"/>), not a reconnect:
    /// re-running OAuth with unchanged scopes does nothing. Also what a
    /// <c>?org=</c> filter naming a user rather than an organization returns.
    /// </summary>
    RepositoryInaccessible,

    /// <summary>
    /// <c>400</c>/<c>422</c> — malformed request, e.g. <c>state=OPEN</c> instead
    /// of <c>state=open</c>, or a PATCH with neither labels nor assignees.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// <c>429</c> — GitHub's rate limit, forwarded. Inferred, not observed. Back
    /// off and retry.
    /// </summary>
    RateLimited
}
