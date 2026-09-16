using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The GitHub panel in Connected Accounts: what the link is, the <b>Reconnect for
/// GitHub Issues</b> handoff, the separate <b>Manage organization access</b>
/// handoff, and an access check that is honest about what it can and cannot
/// prove.
///
/// <para>
/// Composes <see cref="GitHubLinkViewModel"/> rather than re-reading link state,
/// so this panel and the GitHub-backed-list flow can never disagree about whether
/// GitHub is connected.
/// </para>
///
/// <para>
/// <b>Why there is no scope indicator.</b> #76 asks that link state come from
/// <c>GET /api/user/identities</c> and that a scope we cannot detect be said
/// plainly rather than guessed. Both matter here:
/// </para>
/// <list type="bullet">
/// <item><description><c>/api/user/identities</c> is the only per-user signal and
/// carries <b>no scope field</b> — provider, username, profile/avatar URLs,
/// <c>connectedAt</c>, <c>lastVerifiedAt</c>, and that is all (verified live
/// 2026-09-16).</description></item>
/// <item><description><c>/api/auth/github/status</c> is a <b>red herring</b> for
/// this purpose, exactly as CLAUDE.md warns: it answers
/// <c>{ configured: true, clientId: "Ov23li9eXYK1i6psJW6G", manageOrgAccessUrl:
/// "…/settings/connections/applications/Ov23li9eXYK1i6psJW6G" }</c> — server
/// configuration, not user grants. Its one genuinely useful field is
/// <c>manageOrgAccessUrl</c>, which nothing else publishes.</description></item>
/// </list>
/// <para>
/// So a sign-in-only link is indistinguishable from an Issues-capable one until a
/// call behaves differently, and this panel says so instead of drawing a
/// confident green tick.
/// </para>
/// </summary>
public partial class GitHubConnectionViewModel : ObservableObject
{
    private readonly SessionService _session;

    public GitHubLinkViewModel Link { get; }

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private string? accessCheckResult;

    public GitHubConnectionViewModel(SessionService session)
    {
        _session = session;
        Link = new GitHubLinkViewModel(session);
        Link.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GitHubLinkViewModel.LinkState) or nameof(GitHubLinkViewModel.IsLinked))
                OnPropertyChanged(nameof(DefaultRepoLine));
        };
    }

    /// <summary>
    /// The user's <c>githubDefaultRepo</c> — the repository
    /// <c>GET /api/github/issues</c> falls back to when called without one, and a
    /// sensible pre-selection in any repo picker. Null on the test account, so the
    /// "not set" branch is the observed one.
    /// </summary>
    public string DefaultRepoLine => Link.DefaultRepo is { Length: > 0 } repo
        ? $"Default repository: {repo}"
        : "No default repository set — pickers will ask every time.";

    [RelayCommand]
    private async Task LoadAsync() => await Link.LoadAsync();

    /// <summary>
    /// Tries the one read that a sign-in-only link would most plausibly differ on,
    /// and reports <b>exactly</b> what came back — including when the answer is
    /// ambiguous.
    ///
    /// <para>
    /// The ambiguity is real and worth naming rather than papering over: bare
    /// <c>GET /api/github/repos</c> returns <c>200 []</c> on the account this was
    /// built against, which owns no repositories while being properly linked. An
    /// empty array is therefore not evidence of a missing scope, and a non-empty
    /// one is not proof of one either — GitHub lists public repositories without
    /// the <c>repo</c> scope. What this check <em>can</em> do is turn a refusal
    /// into the right remedy, and turn "nothing happened" into a sentence the user
    /// can act on.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task CheckIssuesAccessAsync()
    {
        if (!Link.IsLinked)
        {
            AccessCheckResult = "GitHub isn't connected to this account yet, so there's nothing to check.";
            return;
        }

        IsChecking = true;
        try
        {
            var repos = await _session.Api.GetGitHubReposAsync(null);

            AccessCheckResult = repos.Count > 0
                ? $"{repos.Count:N0} repositor{(repos.Count == 1 ? "y" : "ies")} readable through the link. " +
                  "That confirms the link works — it still can't prove the Issues scope on its own, since " +
                  "public repositories list without it. If creating a GitHub-backed list is refused, reconnect."
                : "No repositories came back, and that's ambiguous: this GitHub account may simply own none " +
                  "(true of the account this app was built against), or the link may be sign-in-only. " +
                  "Reconnect for GitHub Issues to rule the second one out. For an organization's " +
                  "repositories, use the organization filter in the GitHub-backed list flow.";
        }
        catch (InterlinedApiException ex)
        {
            AccessCheckResult = Link.ExplainFailure(ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            AccessCheckResult = "Couldn't reach InterlinedList to check GitHub access.";
        }
        finally
        {
            IsChecking = false;
        }
    }
}
