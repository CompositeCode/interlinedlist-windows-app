using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The strip that sits under a GitHub-backed list's name: the
/// <c>owner/repo issues</c> link, the <b>Private repo</b> tag, and
/// <b>Refresh from GitHub</b>.
///
/// <para>
/// Collapses to nothing for a local list, so it can be hosted unconditionally.
/// </para>
///
/// <para>
/// <b>The private/public flag has three states, not two.</b> /help/lists:
/// "Visibility is re-read from GitHub each time the list syncs… Lists created
/// before this tag existed show no tag until their first sync." So
/// <c>githubRepoPrivate</c> is <c>true</c> (private), <c>false</c> (public) or
/// <c>null</c> (never recorded) and the third case must render <b>no tag</b> —
/// never "public". <see cref="ShowPrivateRepoTag"/> fires only on an explicit
/// <c>true</c>; <see cref="VisibilityUnrecorded"/> is the null case, and it says
/// so rather than staying silent about why there is no tag.
/// </para>
///
/// <para>
/// <b>And the tag is about the repository, not the list.</b> A list can be public
/// while its repository is private, or the reverse — they are set separately. The
/// tag exists to warn that somebody invited to the <em>list</em> may have no
/// access to the <em>repository</em>, so GitHub will show them a sign-in page or
/// a 404 when they follow the link. <see cref="PrivateTagExplanation"/> is that
/// sentence, and it is the whole reason the tag is worth drawing.
/// </para>
/// </summary>
public partial class GitHubListHeaderViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGitHubBacked))]
    [NotifyPropertyChangedFor(nameof(RepoLinkLabel))]
    [NotifyPropertyChangedFor(nameof(ShowPrivateRepoTag))]
    [NotifyPropertyChangedFor(nameof(VisibilityUnrecorded))]
    [NotifyPropertyChangedFor(nameof(RepoVisibilityLine))]
    private GitHubListBacking? backing;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Raised after a successful refresh so the host can reload the list's rows.</summary>
    public event EventHandler? Refreshed;

    public GitHubListHeaderViewModel(SessionService session)
    {
        _session = session;
    }

    public bool IsGitHubBacked => Backing?.IsGitHubBacked == true;

    /// <summary>"owner/repo issues" — the label the web app shows under the list name.</summary>
    public string RepoLinkLabel => Backing?.RepoLinkLabel ?? string.Empty;

    /// <summary>Only ever true for an explicit <c>githubRepoPrivate: true</c>.</summary>
    public bool ShowPrivateRepoTag => Backing?.ShowPrivateRepoTag == true;

    /// <summary>GitHub-backed but visibility never recorded — no tag, and an explanation.</summary>
    public bool VisibilityUnrecorded => Backing?.VisibilityUnrecorded == true;

    /// <summary>
    /// The copy that keeps the two visibilities apart. This is the point of the
    /// tag, so it is spelled out rather than left to a one-word chip.
    /// </summary>
    public string PrivateTagExplanation =>
        "This repository is private on GitHub. That's about the repository, not about who can see " +
        "this InterlinedList list — the two are set separately. Anyone you invite to the list who " +
        "doesn't have repository access will get a GitHub sign-in page or \"not found\" when they " +
        "follow the link.";

    /// <summary>
    /// The line under the link. Says nothing about public/private when the value
    /// has never been recorded — the one thing the null case must not do is imply
    /// the repository is public.
    /// </summary>
    public string RepoVisibilityLine => Backing switch
    {
        { ShowPrivateRepoTag: true } => "Private repo",
        { VisibilityUnrecorded: true } =>
            "Repository visibility hasn't been recorded yet — it's read from GitHub on the next sync. " +
            "Refresh from GitHub to find out.",
        { RepoPrivate: false } => "Public repository on GitHub.",
        _ => string.Empty
    };

    /// <summary>
    /// Loads one list's backing. A null or empty id clears the strip, which is
    /// what "no list selected" looks like.
    /// </summary>
    public async Task LoadAsync(string? listId, CancellationToken ct = default)
    {
        StatusMessage = null;
        ErrorMessage = null;

        if (listId is not { Length: > 0 })
        {
            Backing = null;
            return;
        }

        // Answer instantly from the shared index when it already knows, so
        // selecting a list doesn't wait on a round trip to draw the strip.
        var cached = GitHubListIndex.Get(listId);
        if (cached is not null)
            Backing = cached;

        try
        {
            var fresh = await _session.Api.GetListGitHubBackingAsync(listId, ct);
            Backing = fresh;
            GitHubListIndex.Put(fresh);
        }
        catch (InterlinedApiException ex)
        {
            // Keep whatever the index gave us rather than blanking the strip; a
            // stale repo link is more use than none.
            if (Backing is null)
                ErrorMessage = ex.Message;
            else
                AppLog.Warn($"Re-reading list {listId} for its GitHub backing failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            if (Backing is null)
                ErrorMessage = "Couldn't reach InterlinedList to read this list.";
        }
    }

    /// <summary>
    /// Opens the repository's issues page in the OS browser — the link the web app
    /// puts under the list name.
    /// </summary>
    [RelayCommand]
    private void OpenRepoIssues()
    {
        if (Backing?.IssuesUrl is not { Length: > 0 } url) return;

        try
        {
            // UseShellExecute is required for .NET Core/5+ to hand a URL to the OS
            // default browser.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Opening {url} failed.", ex);
            ErrorMessage = $"Couldn't open your browser. The page is {url}";
        }
    }

    private bool CanRefresh() => !IsBusy && IsGitHubBacked && Backing?.ListId is { Length: > 0 };

    /// <summary>
    /// "Refresh from GitHub" — <c>POST /api/lists/{id}/refresh</c>, then a re-read.
    /// <para>
    /// The re-read is not belt-and-braces: the refresh response envelope has never
    /// been observed (no GitHub-backed list exists on the test account to observe
    /// it with), and re-reading is also the only way to pick up a
    /// <c>githubRepoPrivate</c> that just changed — which is exactly what a sync
    /// is documented to do.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (Backing?.ListId is not { Length: > 0 } listId) return;

        IsBusy = true;
        try
        {
            var result = await _session.Api.RefreshListFromGitHubAsync(listId);

            var before = Backing;
            var fresh = await _session.Api.GetListGitHubBackingAsync(listId);
            Backing = fresh;
            GitHubListIndex.Put(fresh);

            StatusMessage = before?.RepoPrivate != fresh.RepoPrivate
                ? $"{result.DisplayText} Repository visibility is now {(fresh.RepoPrivate == true ? "private" : "public")}."
                : result.DisplayText;
            ErrorMessage = null;

            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (InterlinedApiException ex)
        {
            // The one failure worth naming: refresh on a list that isn't
            // GitHub-backed. CanRefresh should prevent it, so if it happens the
            // list's source changed underneath us.
            ErrorMessage = ex.StatusCode == 400 && ex.Message.Contains("GitHub-backed", StringComparison.OrdinalIgnoreCase)
                ? "This list isn't GitHub-backed, so there's nothing to refresh from."
                : ex.Message;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            ErrorMessage = "Couldn't reach InterlinedList to refresh this list.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    partial void OnBackingChanged(GitHubListBacking? value) => RefreshCommand.NotifyCanExecuteChanged();
}
