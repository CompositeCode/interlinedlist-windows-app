using System.Windows;
using System.Windows.Controls;
using InterlinedList.Services;

namespace InterlinedList.Views;

/// <summary>
/// The GitHub mark that marks a GitHub-backed list in a lists browser
/// (/help/lists: "GitHub-backed lists display a GitHub icon").
///
/// <para>
/// Give it a <see cref="ListId"/> and it answers from
/// <see cref="GitHubListIndex"/> — <b>one</b> request for the whole browser, not
/// one per row. It stays collapsed until the index positively says the list is
/// GitHub-backed, so an unloaded index shows no badge rather than a wrong one.
/// </para>
///
/// <para>
/// Deliberately view-model-less: it has no commands and no state of its own
/// beyond what the index already holds, so a view model would be a layer with
/// nothing in it. It is a projection of shared state onto three dependency
/// properties.
/// </para>
/// </summary>
public partial class GitHubListBadge : UserControl
{
    public GitHubListBadge()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The list this badge describes.</summary>
    public static readonly DependencyProperty ListIdProperty =
        DependencyProperty.Register(nameof(ListId), typeof(string), typeof(GitHubListBadge),
            new PropertyMetadata(null, OnListIdChanged));

    public string? ListId
    {
        get => (string?)GetValue(ListIdProperty);
        set => SetValue(ListIdProperty, value);
    }

    /// <summary>Tooltip naming the backing repository, when one is known.</summary>
    public static readonly DependencyProperty BadgeTooltipProperty =
        DependencyProperty.Register(nameof(BadgeTooltip), typeof(string), typeof(GitHubListBadge),
            new PropertyMetadata("Rows sync from GitHub issues."));

    public string? BadgeTooltip
    {
        get => (string?)GetValue(BadgeTooltipProperty);
        private set => SetValue(BadgeTooltipProperty, value);
    }

    private static void OnListIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GitHubListBadge)d).Apply();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        GitHubListIndex.Changed += OnIndexChanged;
        Apply();

        // Safe to fire and forget: EnsureLoadedAsync swallows its own failures and
        // raises Changed on success, which is what re-applies below.
        await GitHubListIndex.EnsureLoadedAsync();
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // ItemsControl recycles rows, so an unsubscribed badge would leak a handler
        // into a static event for the app's lifetime.
        GitHubListIndex.Changed -= OnIndexChanged;
    }

    private void OnIndexChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        var backing = GitHubListIndex.Get(ListId);

        if (backing is null || !backing.IsGitHubBacked)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        BadgeTooltip = backing.Repo is { Length: > 0 } repo
            ? $"Rows sync from {repo} issues."
            : "Rows sync from GitHub issues.";
    }
}
