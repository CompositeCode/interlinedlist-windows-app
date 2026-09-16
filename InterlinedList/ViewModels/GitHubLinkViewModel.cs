using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The GitHub link state and the two browser handoffs that act on it —
/// "Reconnect for GitHub Issues" and "Manage organization access".
///
/// <para>
/// Shared on purpose: both the GitHub-backed-list flow (which must not let a user
/// reach "Create" only to fail) and Connected Accounts (where the reconnect
/// action lives per /help/lists) need exactly this, and duplicating it would let
/// the two drift apart on the one point that is easy to get wrong — see
/// <see cref="ScopeNote"/>.
/// </para>
///
/// <para>
/// <b>Link state comes from <c>GET /api/user/identities</c>, never from an empty
/// collection.</b> On the test account <c>GET /api/github/repos</c> and
/// <c>GET /api/github/orgs</c> both answer <c>200 []</c> while GitHub <b>is</b>
/// linked (as <c>InterlinedListMessenger</c>, verified live 2026-09-16) — that
/// account simply owns no repositories and joins no organizations. Reading "not
/// connected" out of <c>[]</c> would show a Connect prompt to a connected user.
/// <c>InterlinedApiClient.GetGitHubLinkStateAsync</c> is the one honest signal.
/// </para>
/// </summary>
public partial class GitHubLinkViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked))]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    [NotifyPropertyChangedFor(nameof(CanManageOrgAccess))]
    [NotifyPropertyChangedFor(nameof(DefaultRepo))]
    [NotifyPropertyChangedFor(nameof(ReconnectLabel))]
    private GitHubLinkState? linkState;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Set after a handoff so the UI can say "come back and press Check again".</summary>
    [ObservableProperty]
    private string? handoffNotice;

    public GitHubLinkViewModel(SessionService session)
    {
        _session = session;
    }

    public bool IsLinked => LinkState?.IsLinked == true;

    /// <summary>Null until the first load — distinct from "loaded and not linked".</summary>
    public bool? LinkKnown => LinkState is null ? null : LinkState.IsLinked;

    public string? DefaultRepo => LinkState?.DefaultRepo;

    public bool CanManageOrgAccess => LinkState?.ManageOrgAccessUrl is { Length: > 0 };

    public string StatusLine => LinkState switch
    {
        null when IsLoading => "Checking your GitHub connection…",
        null => "GitHub connection not checked yet.",
        { IsLinked: true } s => $"Connected as @{s.Username ?? "unknown"}",
        { ProviderConfigured: false } => "GitHub sign-in isn't configured on the server.",
        _ => "GitHub isn't connected to this account."
    };

    /// <summary>
    /// The one-line label for the handoff button. "Reconnect" once linked, because
    /// the point is to add the Issues scope to a link that already exists.
    /// </summary>
    public string ReconnectLabel => IsLinked ? "Reconnect for GitHub Issues" : "Connect GitHub";

    /// <summary>
    /// The honest answer to "does this link have the Issues scope?" — <b>we cannot
    /// tell</b>, and saying so beats guessing.
    /// <para>
    /// <c>GET /api/user/identities</c> reports the provider, username and
    /// timestamps, with no scope field. <c>GET /api/auth/github/status</c> reports
    /// <c>{ configured, clientId, manageOrgAccessUrl }</c> — whether the
    /// <em>server</em> has a GitHub OAuth app, not what <em>this user</em>
    /// granted. So there is no scope introspection anywhere in the API, and the
    /// first evidence of a sign-in-only link is a call that comes back empty or
    /// 403/404. Hence the reconnect affordance stays visible even when linked.
    /// </para>
    /// </summary>
    public string ScopeNote =>
        "The API doesn't report which scopes a link has: /api/user/identities lists the provider " +
        "and username, and /api/auth/github/status only says whether the server has GitHub OAuth " +
        "configured. So a sign-in-only link can't be told apart from an Issues-capable one until a " +
        "call comes back empty or refused — reconnect if repositories don't appear.";

    /// <summary>
    /// What <c>?link=true</c> buys, spelled out because it is the whole mechanism
    /// behind the reconnect action.
    /// </summary>
    public string ReconnectExplanation =>
        "Reconnecting opens GitHub in your browser asking for repo and read:org on top of sign-in — " +
        "that's the Issues access a GitHub-backed list needs. Approve it there, then come back and " +
        "press Check again.";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            LinkState = await _session.Api.GetGitHubLinkStateAsync(ct);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            // GetGitHubLinkStateAsync only propagates a failure of
            // /api/user/identities itself — its two enrichment reads are swallowed
            // inside the client — so this really is "we don't know the link state".
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // CLAUDE.md records a real "installs but won't run" crash from catching
            // only InterlinedApiException on a path that can equally throw network
            // or JSON errors. Cancellation is left to propagate.
            ErrorMessage = "Couldn't reach InterlinedList to check your GitHub connection.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    /// <summary>
    /// Opens <c>{base}api/auth/github/authorize?link=true</c> in the OS browser.
    /// <para>
    /// <b><c>?link=true</c> is load-bearing</b> (re-verified live 2026-09-16 by
    /// reading the 307 <c>Location</c> both ways): without it the redirect asks
    /// for <c>scope=user:email read:user</c> — sign-in only; with it,
    /// <c>scope=user:email read:user repo read:org</c> — the Issues scope. A
    /// <c>?scope=</c> of our own is ignored and an arbitrary <c>redirect_uri</c>
    /// is rejected, so the URL is handed over exactly as the server builds it.
    /// </para>
    /// <para>
    /// No WebView2 in this app: OAuth goes to the OS browser and the user comes
    /// back and refreshes, the same pattern the cross-post providers use.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Reconnect()
    {
        try
        {
            InterlinedApiClient.OpenGitHubReconnect();
            HandoffNotice = "GitHub is open in your browser. Approve the repo and read:org scopes, " +
                            "then come back and press Check again.";
        }
        catch (Exception ex)
        {
            // Process.Start can fail with no default browser registered — a broken
            // handoff should say so, not disappear.
            AppLog.Error("Opening the GitHub authorize URL failed.", ex);
            ErrorMessage = $"Couldn't open your browser. Go to {InterlinedApiClient.GitHubReconnectUrl} manually.";
        }
    }

    /// <summary>
    /// Opens GitHub's "Authorized OAuth Apps → InterlinedList" page, the
    /// <b>separate</b> remedy for repositories missing because an organization
    /// hasn't approved the app.
    /// <para>
    /// This is not a second Reconnect button. Re-running OAuth with unchanged
    /// scopes returns silently without changing organization access, so a
    /// reconnect genuinely cannot fix it — which is why
    /// <c>/api/auth/github/status</c> publishes <c>manageOrgAccessUrl</c>
    /// (<c>https://github.com/settings/connections/applications/{clientId}</c>,
    /// verified live 2026-09-16) and why this is its own action.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ManageOrgAccess()
    {
        if (LinkState?.ManageOrgAccessUrl is not { Length: > 0 } url) return;

        try
        {
            // UseShellExecute is required for .NET Core/5+ to hand a URL to the
            // OS default browser.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            HandoffNotice = "GitHub's authorized-apps page is open. Grant InterlinedList access to the " +
                            "organization, then come back and press Check again.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Opening the GitHub org-access URL failed.", ex);
            ErrorMessage = $"Couldn't open your browser. Go to {url} manually.";
        }
    }

    /// <summary>
    /// Turns a failed <c>/api/github/*</c> call into the remedy that actually
    /// applies, rather than a generic error toast.
    /// </summary>
    public string ExplainFailure(InterlinedApiException ex) =>
        InterlinedApiClient.ClassifyGitHubFailure(ex) switch
        {
            GitHubFailureReason.NotLinked =>
                "GitHub isn't connected to this account. Connect it, then try again.",
            GitHubFailureReason.NotAuthenticated =>
                "Your InterlinedList session was rejected. Sign in again.",
            GitHubFailureReason.RepositoryInaccessible =>
                "GitHub wouldn't show that. Either the name isn't an organization (a personal " +
                "account's repositories come from \"My repositories\", not an org filter), or the " +
                "organization hasn't approved InterlinedList — which only \"Manage organization " +
                "access\" can fix, not reconnecting.",
            GitHubFailureReason.RepositoryRequired =>
                "That call needs a repository. Pick one, or set a default repo in Settings.",
            GitHubFailureReason.InvalidRequest =>
                $"GitHub rejected the request: {ex.Message}",
            GitHubFailureReason.RateLimited =>
                "GitHub is rate-limiting this account. Wait a minute and try again.",
            _ => ex.Message
        };
}
