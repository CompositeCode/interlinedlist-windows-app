using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The repository picker behind the GitHub-backed-list flow: "My repositories"
/// or an organization's, filtered down by typing, one selection out.
///
/// <para>
/// <b>Paging: there is none to do, and the epic was wrong in both directions.</b>
/// #73 asked for a "paginated" picker because the docs call the endpoint "fully
/// paginated across all affiliations". The server does that walking itself and
/// returns the complete set in <em>one bare array</em> — 559 repositories for
/// <c>?org=github</c>, 278 for <c>?org=dotnet</c>, well past GitHub's own
/// 100-per-page ceiling. There is no envelope, no <c>Link</c> header, and
/// <c>?page=</c>/<c>?per_page=</c> change nothing (probed live 2026-09-15 and
/// re-confirmed 2026-09-16). So this loads once and filters locally.
/// </para>
///
/// <para>
/// <b>But an <c>?org=</c> is required to see anything but your own.</b> Bare
/// <c>GET /api/github/repos</c> returns only the linked account's own
/// repositories — <c>[]</c> on the test account, which owns none. That empty
/// array is <b>not</b> evidence of a missing link (see
/// <see cref="GitHubLinkViewModel"/>), so the empty state says so instead of
/// offering a Connect button.
/// </para>
///
/// <para>
/// <b>The 2000 cap is real.</b> Six organizations have now each returned exactly
/// 2000 rows — <c>microsoft</c>, <c>google</c>, <c>apache</c> (2026-09-15) plus
/// <c>Azure</c>, <c>mozilla</c>, <c>IBM</c> (2026-09-16) — while
/// <c>aws</c> (552), <c>elastic</c> (958), <c>hashicorp</c> (943),
/// <c>adobe</c> (1123) and <c>intel</c> (1360) returned natural counts. Exactly
/// 2000 is therefore a truncation, not a total, and
/// <see cref="ResultSummary"/> says "first 2000" rather than implying the list is
/// complete.
/// </para>
///
/// <para>
/// <c>?org=</c> must name an <b>organization</b>: a user login answers
/// <c>404 not_found</c> (GitHub's <c>/orgs/{org}/repos</c> has no such org), which
/// <see cref="GitHubLinkViewModel.ExplainFailure"/> turns into that sentence
/// rather than "not found".
/// </para>
/// </summary>
public partial class GitHubRepoPickerViewModel : ObservableObject
{
    /// <summary>
    /// The server's apparent per-request cap. Six large organizations have each
    /// returned exactly this many — see the class remarks.
    /// </summary>
    public const int ServerRepoCap = 2000;

    /// <summary>
    /// How many filtered rows to hand the UI at once. The <c>ListBox</c> hosting
    /// them virtualizes, so this is about keeping the "showing N of M" line
    /// honest rather than about performance.
    /// </summary>
    private const int MaxVisible = 400;

    private readonly SessionService _session;
    private readonly GitHubLinkViewModel _link;
    private readonly List<GitHubRepo> _all = [];

    /// <summary>Organizations the linked account belongs to, for the drop-down.</summary>
    public ObservableCollection<GitHubOrg> Orgs { get; } = new();

    /// <summary>The filtered slice the picker actually shows.</summary>
    public ObservableCollection<GitHubRepo> VisibleRepos { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>An organization login typed by hand — needed because <c>GET /api/github/orgs</c>
    /// is empty for an account that belongs to no organizations, yet any public
    /// org's repositories are still readable (<c>?org=github</c> returned 559 from
    /// exactly such an account).</summary>
    [ObservableProperty]
    private string orgLogin = "";

    [ObservableProperty]
    private string filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GitHubRepo? selectedRepo;

    /// <summary>Which scope the currently-loaded set came from, for the summary line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private string? loadedScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    [NotifyPropertyChangedFor(nameof(HasLoaded))]
    private bool hasLoadedOnce;

    public GitHubRepoPickerViewModel(SessionService session, GitHubLinkViewModel link)
    {
        _session = session;
        _link = link;
    }

    public bool HasSelection => SelectedRepo is not null;

    public bool HasLoaded => HasLoadedOnce;

    /// <summary>True when the loaded set was truncated by the server's cap.</summary>
    public bool IsCapped => _all.Count >= ServerRepoCap;

    /// <summary>
    /// The line under the picker. Never claims completeness at exactly the cap,
    /// and never reads an empty result as a broken connection.
    /// </summary>
    public string ResultSummary
    {
        get
        {
            if (!HasLoadedOnce) return "Load your repositories, or an organization's, to pick one.";

            var scope = LoadedScope is { Length: > 0 } s ? s : "your repositories";

            if (_all.Count == 0)
            {
                return scope == MyReposScope
                    ? "That GitHub account owns no repositories. It is still connected — an empty list " +
                      "isn't a broken link. Try an organization, or reconnect if you expected repo access."
                    : $"No repositories came back for {scope}.";
            }

            var shown = VisibleRepos.Count;
            var capNote = IsCapped
                ? $" The server returns at most {ServerRepoCap:N0} per organization, so this is the " +
                  "first 2,000, not all of them — narrow it with the filter."
                : string.Empty;

            return shown == _all.Count
                ? $"{_all.Count:N0} from {scope}.{capNote}"
                : $"Showing {shown:N0} of {_all.Count:N0} from {scope}.{capNote}";
        }
    }

    private const string MyReposScope = "your repositories";

    /// <summary>
    /// The organizations the account belongs to. Empty on the test account, and
    /// the item shape has therefore never been observed — hence
    /// <see cref="GitHubOrg"/>'s all-nullable fields and the typed-login box
    /// beside this drop-down.
    /// </summary>
    [RelayCommand]
    private async Task LoadOrgsAsync(CancellationToken ct = default)
    {
        try
        {
            var orgs = await _session.Api.GetGitHubOrgsAsync(ct);

            Orgs.Clear();
            foreach (var org in orgs.Where(o => o.FilterValue.Length > 0))
                Orgs.Add(org);
        }
        catch (InterlinedApiException ex)
        {
            // Non-fatal: the typed-login box still works without the drop-down.
            AppLog.Warn($"GET /api/github/orgs failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            AppLog.Warn($"GET /api/github/orgs failed: {ex.Message}");
        }
    }

    /// <summary>The linked account's own repositories — bare <c>GET /api/github/repos</c>.</summary>
    [RelayCommand]
    private Task LoadMyReposAsync(CancellationToken ct = default) => LoadAsync(null, ct);

    private bool CanLoadOrgRepos() => !string.IsNullOrWhiteSpace(OrgLogin);

    /// <summary>One organization's repositories — <c>GET /api/github/repos?org=</c>.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadOrgRepos))]
    private Task LoadOrgReposAsync(CancellationToken ct = default) => LoadAsync(OrgLogin.Trim(), ct);

    /// <summary>Loads an org's repositories straight from the drop-down.</summary>
    [RelayCommand]
    private Task PickOrgAsync(GitHubOrg org)
    {
        OrgLogin = org.FilterValue;
        return LoadAsync(org.FilterValue);
    }

    private async Task LoadAsync(string? org, CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var repos = await _session.Api.GetGitHubReposAsync(org, ct);

            _all.Clear();
            // owner/name order so a filtered 2000-row org reads predictably.
            _all.AddRange(repos.OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase));

            LoadedScope = org is { Length: > 0 } ? $"the {org} organization" : MyReposScope;
            HasLoadedOnce = true;
            SelectedRepo = null;
            ErrorMessage = null;
            ApplyFilter();
            PreselectDefaultRepo();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = _link.ExplainFailure(ex);
            _all.Clear();
            HasLoadedOnce = true;
            LoadedScope = org is { Length: > 0 } ? $"the {org} organization" : MyReposScope;
            ApplyFilter();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            ErrorMessage = "Couldn't reach InterlinedList to list repositories.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ResultSummary));
            OnPropertyChanged(nameof(IsCapped));
        }
    }

    /// <summary>
    /// Pre-selects the user's <c>githubDefaultRepo</c> when it happens to be in
    /// the loaded set — the same value <c>GET /api/github/issues</c> falls back to
    /// when called without a <c>repo</c>. Null on the test account, so this is
    /// built from the documented field rather than from an observed value.
    /// </summary>
    private void PreselectDefaultRepo()
    {
        if (_link.DefaultRepo is not { Length: > 0 } preferred) return;

        var match = _all.FirstOrDefault(r =>
            string.Equals(r.FullName, preferred, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            SelectedRepo = match;
    }

    private void ApplyFilter()
    {
        var needle = FilterText.Trim();

        IEnumerable<GitHubRepo> matches = needle.Length == 0
            ? _all
            : _all.Where(r =>
                r.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));

        VisibleRepos.Clear();
        foreach (var repo in matches.Take(MaxVisible))
            VisibleRepos.Add(repo);

        OnPropertyChanged(nameof(ResultSummary));
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnOrgLoginChanged(string value) => LoadOrgReposCommand.NotifyCanExecuteChanged();
}
