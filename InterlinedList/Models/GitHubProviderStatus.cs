namespace InterlinedList.Models;

/// <summary>
/// GET /api/auth/github/status → <c>{ configured, clientId, manageOrgAccessUrl }</c>
/// (verified live 2026-09-15, 200 with the bearer token).
/// <para>
/// This is an InterlinedList-shaped (camelCase) response, not a GitHub-shaped
/// one, so it needs no <c>JsonPropertyName</c> attributes — the client's
/// <c>JsonSerializerDefaults.Web</c> options map it, the same as every other
/// model in this folder.
/// </para>
/// <para>
/// As CLAUDE.md warns, <c>configured</c> reports whether the <em>server</em> has
/// the GitHub OAuth app set up — <b>not</b> whether this user linked GitHub. Use
/// <see cref="GitHubLinkState"/> for the per-user answer. What makes this
/// endpoint worth calling anyway is <see cref="ManageOrgAccessUrl"/>: it is the
/// only place the deep link for granting organization access is published.
/// </para>
/// </summary>
public sealed class GitHubProviderStatus
{
    /// <summary>Whether the server has a GitHub OAuth app configured at all.</summary>
    public bool Configured { get; init; }

    /// <summary>The OAuth app's public client id (e.g. <c>Ov23li9eXYK1i6psJW6G</c>).</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// GitHub's "Authorized OAuth Apps → InterlinedList" page, e.g.
    /// <c>https://github.com/settings/connections/applications/{clientId}</c>.
    /// Per /help/api/github-integration this is where a user grants or requests
    /// <b>organization access</b> — and re-running the OAuth flow will not do it
    /// for them, because GitHub returns silently when the scopes are unchanged.
    /// So an org whose repos are missing from the picker is fixed here, not by
    /// reconnecting.
    /// </summary>
    public string? ManageOrgAccessUrl { get; init; }
}
