using System.Windows;
using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The GitHub panel for Connected Accounts — status, <b>Reconnect for GitHub
/// Issues</b>, <b>Manage organization access</b>, and an honest access check.
///
/// <para>
/// Self-contained with its own view model, so hosting costs one element and
/// <c>ConnectedAccountsViewModel</c> needs no changes at all. That matters here
/// beyond tidiness: PR #154 is editing that view model's <c>LoadAsync</c> and
/// appending a LinkedIn panel to the end of the same view, so this lands in a
/// different region of the file and touches none of the same members.
/// </para>
///
/// <para>
/// <see cref="ReloadSignal"/> is the one piece of host coupling, and it is
/// deliberately valueless: bind it to anything that changes when the host
/// reloads — <c>{Binding Identities.Count}</c> works, because
/// <c>ObservableCollection</c> raises <c>PropertyChanged</c> for <c>Count</c> on
/// both the <c>Clear()</c> and the re-fill — and the panel refreshes alongside
/// the host's existing Refresh button. It also has its own <b>Check again</b>, so
/// the binding is optional.
/// </para>
/// </summary>
public partial class GitHubConnectionPanel : UserControl
{
    private readonly GitHubConnectionViewModel _vm;

    public GitHubConnectionPanel()
    {
        InitializeComponent();

        _vm = new GitHubConnectionViewModel(Services.AppServices.Session);
        DataContext = _vm;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Any value that changes when the host refreshes; the panel reloads in step.
    /// The value itself is never read.
    /// </summary>
    public static readonly DependencyProperty ReloadSignalProperty =
        DependencyProperty.Register(nameof(ReloadSignal), typeof(object), typeof(GitHubConnectionPanel),
            new PropertyMetadata(null, OnReloadSignalChanged));

    public object? ReloadSignal
    {
        get => GetValue(ReloadSignalProperty);
        set => SetValue(ReloadSignalProperty, value);
    }

    private static void OnReloadSignalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (GitHubConnectionPanel)d;

        // Only once the control is live, so the initial binding pass doesn't
        // duplicate the load in OnLoaded.
        if (panel.IsLoaded)
            _ = panel._vm.LoadCommand.ExecuteAsync(null);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again when the view is swapped back in, which is exactly
        // when a re-read is wanted: the user may have been away in the browser
        // approving scopes.
        await _vm.LoadCommand.ExecuteAsync(null);
    }
}
