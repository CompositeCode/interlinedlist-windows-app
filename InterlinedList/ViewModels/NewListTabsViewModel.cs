using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The two-tab new-list flow from /help/lists — <b>Local List</b> and
/// <b>GitHub-backed List</b> — of which this owns the tab choice and the whole
/// GitHub tab. The Local tab is the host's existing title/description form, which
/// this only shows and hides via <see cref="IsLocalTabSelected"/>; rebuilding it
/// would have meant a much larger edit to a heavily contended view for no gain.
///
/// <para>
/// The GitHub tab follows the documented flow exactly: pick a repository, a title
/// that <b>defaults to the repository name</b>, an optional parent list, and a
/// Public list toggle.
/// </para>
///
/// <para>
/// <b>Never executed live.</b> Creating a GitHub-backed list would wire a real
/// GitHub repository to a real list on shared test infrastructure, so
/// <see cref="CreateGitHubListCommand"/> is built and left unexercised. The
/// contract it targets <em>was</em> verified without creating anything:
/// <c>POST /api/lists { title, source: "github" }</c> with no <c>githubRepo</c>
/// answers <c>400 "githubRepo is required for GitHub-backed lists (format:
/// owner/repo)"</c> and creates nothing (2026-09-16).
/// </para>
///
/// <para>
/// <b>On "detect the missing Issues scope up front".</b> That is #73's acceptance
/// criterion and it cannot be met literally — the API exposes no scope
/// introspection at all (see <see cref="GitHubLinkViewModel.ScopeNote"/>). What
/// it does instead, which is the useful half: gate the tab on real link state
/// from <c>/api/user/identities</c>, keep the reconnect handoff visible the whole
/// time rather than only after a failure, and when a repository load comes back
/// empty or refused, name the specific remedy — reconnect for the Issues scope,
/// or grant organization access, which a reconnect cannot fix.
/// </para>
/// </summary>
public partial class NewListTabsViewModel : ObservableObject
{
    private readonly SessionService _session;

    /// <summary>The repo name the title was auto-filled from, so a user's own typing is never overwritten.</summary>
    private string? _autoFilledFrom;

    public GitHubLinkViewModel Link { get; }

    public GitHubRepoPickerViewModel Picker { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalTabSelected))]
    private bool isGitHubTabSelected;

    [ObservableProperty]
    private string gitHubTitle = "";

    [ObservableProperty]
    private string gitHubDescription = "";

    [ObservableProperty]
    private ListSummary? selectedParentList;

    [ObservableProperty]
    private bool isPublic;

    [ObservableProperty]
    private bool isCreating;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? successMessage;

    /// <summary>Raised after a successful create so the host can reload its lists.</summary>
    public event EventHandler? ListCreated;

    public NewListTabsViewModel(SessionService session)
    {
        _session = session;
        Link = new GitHubLinkViewModel(session);
        Picker = new GitHubRepoPickerViewModel(session, Link);
        Picker.PropertyChanged += OnPickerPropertyChanged;
        Link.PropertyChanged += OnLinkPropertyChanged;
    }

    /// <summary>The host binds its own form's visibility to this.</summary>
    public bool IsLocalTabSelected => !IsGitHubTabSelected;

    public string SelectedRepoLabel => Picker.SelectedRepo?.FullName ?? "No repository picked yet.";

    /// <summary>
    /// The private/public state of the <b>repository on GitHub</b>, shown while
    /// picking so the "Private repo" tag on the finished list isn't a surprise.
    /// This is not the list's own visibility — <see cref="IsPublic"/> is that, and
    /// the two are set separately.
    /// </summary>
    public bool SelectedRepoIsPrivate => Picker.SelectedRepo?.IsPrivate == true;

    public bool CanCreate =>
        !IsCreating &&
        Picker.SelectedRepo is not null &&
        !string.IsNullOrWhiteSpace(GitHubTitle) &&
        Link.IsLinked;

    [RelayCommand]
    private void SelectLocalTab() => IsGitHubTabSelected = false;

    /// <summary>
    /// Switching to the GitHub tab is what triggers the link check and the org
    /// list — the point of checking before "Create" rather than after it.
    /// </summary>
    [RelayCommand]
    private async Task SelectGitHubTabAsync()
    {
        IsGitHubTabSelected = true;

        if (Link.LinkState is null)
            await Link.LoadAsync();

        if (Link.IsLinked && Picker.Orgs.Count == 0)
            await Picker.LoadOrgsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CheckConnectionAsync()
    {
        await Link.LoadAsync();
        if (Link.IsLinked && Picker.Orgs.Count == 0)
            await Picker.LoadOrgsCommand.ExecuteAsync(null);
        OnPropertyChanged(nameof(CanCreate));
    }

    [RelayCommand]
    private void ClearParentList() => SelectedParentList = null;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateGitHubListAsync()
    {
        if (Picker.SelectedRepo is not { } repo)
        {
            ErrorMessage = "Pick a repository first.";
            return;
        }

        if (!Link.IsLinked)
        {
            // Up front, not at the API: a create with no GitHub link cannot work,
            // and the remedy is the handoff sitting right above this button.
            ErrorMessage = "Connect GitHub before creating a GitHub-backed list.";
            return;
        }

        IsCreating = true;
        try
        {
            var created = await _session.Api.CreateGitHubBackedListAsync(
                repo.FullName,
                GitHubTitle,
                string.IsNullOrWhiteSpace(GitHubDescription) ? null : GitHubDescription,
                SelectedParentList?.Id,
                IsPublic);

            // Read-after-write, per this repo's rule for unverified envelopes: the
            // returned refreshStatus has never been observed, so the list's real
            // state comes from re-reading it.
            var status = created.RefreshStatus is { Length: > 0 } s ? $" Initial sync: {s}." : string.Empty;
            SuccessMessage = $"Created \"{GitHubTitle.Trim()}\" from {repo.FullName}.{status}";
            ErrorMessage = null;

            GitHubTitle = "";
            GitHubDescription = "";
            SelectedParentList = null;
            IsPublic = false;
            Picker.SelectedRepo = null;
            _autoFilledFrom = null;

            // The badge cache is now stale by exactly one list.
            await GitHubListIndex.RefreshAsync();
            ListCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            // The client's own owner/repo guard, which mirrors the server's 400.
            ErrorMessage = ex.Message;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = Link.ExplainFailure(ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            ErrorMessage = "Couldn't reach InterlinedList to create the list.";
        }
        finally
        {
            IsCreating = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    private void OnLinkPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(GitHubLinkViewModel.LinkState) or nameof(GitHubLinkViewModel.IsLinked)))
            return;

        OnPropertyChanged(nameof(CanCreate));
        CreateGitHubListCommand.NotifyCanExecuteChanged();
    }

    private void OnPickerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GitHubRepoPickerViewModel.SelectedRepo)) return;

        ApplyTitleDefault();
        OnPropertyChanged(nameof(SelectedRepoLabel));
        OnPropertyChanged(nameof(SelectedRepoIsPrivate));
        OnPropertyChanged(nameof(CanCreate));
        CreateGitHubListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// "Title defaults to the repo name" (/help/lists) — without clobbering a
    /// title the user typed. Only an empty box, or one still holding the previous
    /// repository's auto-filled name, gets replaced.
    /// </summary>
    private void ApplyTitleDefault()
    {
        var name = Picker.SelectedRepo?.Name;
        if (name is not { Length: > 0 }) return;

        var untouched = string.IsNullOrWhiteSpace(GitHubTitle) ||
                        string.Equals(GitHubTitle, _autoFilledFrom, StringComparison.Ordinal);

        if (!untouched) return;

        GitHubTitle = name;
        _autoFilledFrom = name;
    }

    partial void OnGitHubTitleChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        CreateGitHubListCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCreatingChanged(bool value) => OnPropertyChanged(nameof(CanCreate));
}
