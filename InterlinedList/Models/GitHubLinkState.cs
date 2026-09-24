namespace InterlinedList.Models;

/// <summary>
/// Whether this user has a usable GitHub link — the <b>distinguishable state</b>
/// the GitHub UI needs so that "you haven't connected GitHub" never has to be
/// inferred from a generic error. Composed by
/// <c>InterlinedApiClient.GetGitHubLinkStateAsync</c> from three live-verified
/// reads: <c>GET /api/user/identities</c> (the per-user truth),
/// <c>GET /api/auth/github/status</c> (server config + the org-access deep link)
/// and <c>GET /api/user</c> (the per-user default repo).
/// <para>
/// This is a composed result, not a wire type — no single endpoint returns it.
/// </para>
/// <para>
/// <b>Why it is built from identities rather than from a failed call.</b> The
/// nine <c>/api/github/*</c> endpoints answer the bearer sync-token cleanly and
/// return <em>empty collections</em> when there is nothing to show, so an empty
/// repo list is genuinely ambiguous — on the test account
/// <c>GET /api/github/repos</c> returns <c>200 []</c> even though GitHub
/// <b>is</b> linked (as <c>InterlinedListMessenger</c>), simply because that
/// account owns no repositories. "Empty" therefore cannot be read as "not
/// linked", and <c>/api/user/identities</c> is the only unambiguous signal.
/// </para>
/// </summary>
public sealed class GitHubLinkState
{
    /// <summary>
    /// True when <c>GET /api/user/identities</c> lists a <c>github</c> provider
    /// for this user. The one reliable per-user answer.
    /// </summary>
    public bool IsLinked { get; init; }

    /// <summary>Server-side OAuth app configuration, from <see cref="GitHubProviderStatus.Configured"/>.</summary>
    public bool ProviderConfigured { get; init; }

    /// <summary>The linked GitHub login, e.g. <c>InterlinedListMessenger</c>.</summary>
    public string? Username { get; init; }

    public string? ProfileUrl { get; init; }

    public string? AvatarUrl { get; init; }

    public DateTimeOffset? ConnectedAt { get; init; }

    /// <summary>
    /// When the server last successfully exercised the stored GitHub token. A
    /// stale value alongside failing calls hints at a revoked authorization.
    /// </summary>
    public DateTimeOffset? LastVerifiedAt { get; init; }

    /// <summary>
    /// <c>githubDefaultRepo</c> from <c>GET /api/user</c> — the repo the server
    /// falls back to when <c>GET /api/github/issues</c> is called with no
    /// <c>repo</c> parameter, and a sensible pre-selection for any repo picker.
    /// Null on the test account (verified live 2026-09-15).
    /// <para>
    /// Read out of the raw <c>/api/user</c> JSON rather than from
    /// <c>CurrentUser</c>: that model does not carry the field yet and is being
    /// changed in another open PR, so it is deliberately left alone here.
    /// </para>
    /// </summary>
    public string? DefaultRepo { get; init; }

    /// <summary>
    /// GitHub's "Authorized OAuth Apps" page for this InterlinedList install —
    /// where organization access is granted. See
    /// <see cref="GitHubProviderStatus.ManageOrgAccessUrl"/>.
    /// </summary>
    public string? ManageOrgAccessUrl { get; init; }

    /// <summary>
    /// The browser handoff URL for "Connect GitHub" / "Reconnect for GitHub
    /// Issues": <c>{base}api/auth/github/authorize?link=true</c>.
    /// <para>
    /// <b>The <c>link=true</c> query parameter is load-bearing</b> (verified live
    /// 2026-09-15 by reading the 307 <c>Location</c> header both ways):
    /// </para>
    /// <list type="bullet">
    /// <item><description>without it, the redirect requests
    /// <c>scope=user:email read:user</c> — sign-in only, no issue access;</description></item>
    /// <item><description>with it, the redirect requests
    /// <c>scope=user:email read:user repo read:org</c> — the Issues-capable
    /// scope the GitHub-backed-list feature needs.</description></item>
    /// </list>
    /// <para>
    /// A <c>?scope=</c> parameter of your own is ignored, and an arbitrary
    /// <c>redirect_uri</c> is rejected (the server bounces to
    /// <c>/login?error=Invalid redirect_uri</c>), so hand over exactly this URL.
    /// </para>
    /// </summary>
    public required string ReconnectUrl { get; init; }

    /// <summary>
    /// Whether offering the reconnect handoff makes sense: pointless if the
    /// server has no GitHub OAuth app configured.
    /// </summary>
    public bool CanReconnect => ProviderConfigured;

    /// <summary>
    /// A link exists but may lack the Issues scope. The API exposes no scope
    /// introspection (<c>/api/auth/github/status</c> reports server config only),
    /// so this cannot be known ahead of a failure — it is the cue to keep the
    /// "Reconnect for GitHub Issues" affordance visible even when linked, which
    /// is exactly what the web app does per /help/lists.
    /// </summary>
    public bool MayNeedIssuesScope => IsLinked;

    public string StatusLabel => IsLinked
        ? $"Connected as @{Username ?? "unknown"}"
        : "GitHub not connected";
}
