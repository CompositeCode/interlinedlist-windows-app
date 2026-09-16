using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The two-tab new-list flow — <b>Local List</b> / <b>GitHub-backed List</b> —
/// as a self-contained control that owns its own view model.
///
/// <para>
/// <b>Why a control and not code in <c>ListsView</c>.</b> <c>ListsView.xaml</c> is
/// being edited by several open PRs at once, so this follows the pattern that
/// worked on the list/document invite panels: everything lives here, context
/// arrives through dependency properties, and hosting costs one element plus a
/// visibility binding on the form this replaces on the GitHub tab. Nothing is
/// added to <c>ListsViewModel</c> and no code-behind of the host changes.
/// </para>
///
/// <para>
/// Hosting contract, all optional:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ParentListSource"/> — the host's list collection,
/// for the "Parent list" drop-down.</description></item>
/// <item><description><see cref="ListsRefreshCommand"/> — run after a successful
/// create, so the host's browser picks the new list up.</description></item>
/// <item><description><see cref="IsLocalTabSelected"/> — read by the host to show
/// or hide its own local create form, which <em>is</em> the Local tab.</description></item>
/// </list>
/// </summary>
public partial class NewListTabs : UserControl
{
    private readonly NewListTabsViewModel _vm;

    public NewListTabs()
    {
        InitializeComponent();

        _vm = new NewListTabsViewModel(Services.AppServices.Session);
        DataContext = _vm;

        // Mirror the tab choice out as a dependency property so the host can bind
        // its own form's Visibility to it with one attribute.
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ListCreated += OnListCreated;

        IsLocalTabSelected = _vm.IsLocalTabSelected;
    }

    /// <summary>The host's lists, offered as parent candidates. Any enumerable of <c>ListSummary</c>.</summary>
    public static readonly DependencyProperty ParentListSourceProperty =
        DependencyProperty.Register(nameof(ParentListSource), typeof(IEnumerable), typeof(NewListTabs),
            new PropertyMetadata(null));

    public IEnumerable? ParentListSource
    {
        get => (IEnumerable?)GetValue(ParentListSourceProperty);
        set => SetValue(ParentListSourceProperty, value);
    }

    /// <summary>
    /// Executed after a successful create. The panel deliberately does not reach
    /// into the host's view model itself — the host says how to refresh.
    /// </summary>
    public static readonly DependencyProperty ListsRefreshCommandProperty =
        DependencyProperty.Register(nameof(ListsRefreshCommand), typeof(ICommand), typeof(NewListTabs),
            new PropertyMetadata(null));

    public ICommand? ListsRefreshCommand
    {
        get => (ICommand?)GetValue(ListsRefreshCommandProperty);
        set => SetValue(ListsRefreshCommandProperty, value);
    }

    /// <summary>
    /// True while the <b>Local List</b> tab is chosen. Read-only in practice — the
    /// panel sets it; the host binds to it.
    /// </summary>
    public static readonly DependencyProperty IsLocalTabSelectedProperty =
        DependencyProperty.Register(nameof(IsLocalTabSelected), typeof(bool), typeof(NewListTabs),
            new PropertyMetadata(true));

    public bool IsLocalTabSelected
    {
        get => (bool)GetValue(IsLocalTabSelectedProperty);
        private set => SetValue(IsLocalTabSelectedProperty, value);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NewListTabsViewModel.IsLocalTabSelected)
                           or nameof(NewListTabsViewModel.IsGitHubTabSelected))
        {
            IsLocalTabSelected = _vm.IsLocalTabSelected;
        }
    }

    private void OnListCreated(object? sender, EventArgs e)
    {
        if (ListsRefreshCommand?.CanExecute(null) == true)
            ListsRefreshCommand.Execute(null);
    }
}
