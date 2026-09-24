using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The <c>owner/repo issues</c> link, <b>Private repo</b> tag and <b>Refresh from
/// GitHub</b> action for a GitHub-backed list — self-contained, with its own view
/// model, so hosting it costs one element.
///
/// <para>
/// Hosting contract:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ListId"/> — bind to the selected list's id. It
/// reloads on change and collapses itself for a local list or a null id, so it
/// can be dropped in unconditionally.</description></item>
/// <item><description><see cref="AfterRefreshCommand"/> /
/// <see cref="AfterRefreshCommandParameter"/> — run after a successful refresh.
/// A refresh rewrites the list's rows from the repository's issues, so the host
/// needs to reload them; this control deliberately doesn't know how.</description></item>
/// </list>
/// </summary>
public partial class GitHubListHeader : UserControl
{
    private readonly GitHubListHeaderViewModel _vm;

    public GitHubListHeader()
    {
        InitializeComponent();

        _vm = new GitHubListHeaderViewModel(Services.AppServices.Session);
        DataContext = _vm;
        _vm.Refreshed += OnRefreshed;
    }

    /// <summary>The list to describe. Changing it reloads the strip.</summary>
    public static readonly DependencyProperty ListIdProperty =
        DependencyProperty.Register(nameof(ListId), typeof(string), typeof(GitHubListHeader),
            new PropertyMetadata(null, OnListIdChanged));

    public string? ListId
    {
        get => (string?)GetValue(ListIdProperty);
        set => SetValue(ListIdProperty, value);
    }

    /// <summary>Executed after a successful refresh, so the host can reload rows.</summary>
    public static readonly DependencyProperty AfterRefreshCommandProperty =
        DependencyProperty.Register(nameof(AfterRefreshCommand), typeof(ICommand), typeof(GitHubListHeader),
            new PropertyMetadata(null));

    public ICommand? AfterRefreshCommand
    {
        get => (ICommand?)GetValue(AfterRefreshCommandProperty);
        set => SetValue(AfterRefreshCommandProperty, value);
    }

    /// <summary>Parameter for <see cref="AfterRefreshCommand"/> — typically the selected list.</summary>
    public static readonly DependencyProperty AfterRefreshCommandParameterProperty =
        DependencyProperty.Register(nameof(AfterRefreshCommandParameter), typeof(object), typeof(GitHubListHeader),
            new PropertyMetadata(null));

    public object? AfterRefreshCommandParameter
    {
        get => GetValue(AfterRefreshCommandParameterProperty);
        set => SetValue(AfterRefreshCommandParameterProperty, value);
    }

    private static async void OnListIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (GitHubListHeader)d;

        // async void on a DP callback is the one place WPF leaves no alternative;
        // LoadAsync catches everything it can throw and reports it on the view
        // model, so nothing escapes to the dispatcher's unhandled handler.
        await header._vm.LoadAsync(e.NewValue as string);
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        var parameter = AfterRefreshCommandParameter;
        if (AfterRefreshCommand?.CanExecute(parameter) == true)
            AfterRefreshCommand.Execute(parameter);
    }
}
