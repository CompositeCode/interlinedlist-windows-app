using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The Powered Templates surface: the web app's "Powered Templates tab in the
/// new-list flow", as a self-contained card.
///
/// <b>Hosting it costs one XAML line and no code-behind:</b>
/// <code>
/// &lt;local:PoweredTemplatePanel OpenListCommand="{Binding SelectListCommand}"
///                            RefreshCommand="{Binding LoadListsCommand}"/&gt;
/// </code>
///
/// That shape is deliberate. <c>Views/ListsView.xaml</c> is contended by
/// several other open PRs (#161/#163/#164), so this follows the pattern that
/// worked on #55/#56: everything lives in the control and its own ViewModel,
/// and the host contributes a single line it can move anywhere. The control
/// news up its own ViewModel from <see cref="AppServices"/> rather than having
/// one injected, matching every other view in this folder.
///
/// It hides itself entirely when the AI gate is closed (free account, or a
/// server with no Anthropic key), so the host needs no visibility rule either.
/// </summary>
public partial class PoweredTemplatePanel : UserControl
{
    /// <summary>
    /// Invoked with the freshly re-fetched <see cref="Models.ListSummary"/>
    /// after a confirmed generation, so the host can select the new list. Bind
    /// it to the host's existing "select this list" command.
    /// </summary>
    public static readonly DependencyProperty OpenListCommandProperty =
        DependencyProperty.Register(
            nameof(OpenListCommand), typeof(ICommand), typeof(PoweredTemplatePanel),
            new PropertyMetadata(null, OnOpenListCommandChanged));

    /// <summary>
    /// Invoked with null after a confirmed generation so the host reloads its
    /// list browser. Bind it to the host's existing reload command.
    /// </summary>
    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.Register(
            nameof(RefreshCommand), typeof(ICommand), typeof(PoweredTemplatePanel),
            new PropertyMetadata(null, OnRefreshCommandChanged));

    private readonly PoweredTemplatePanelViewModel _vm;

    public PoweredTemplatePanel()
    {
        InitializeComponent();
        _vm = new PoweredTemplatePanelViewModel(AppServices.Session);
        DataContext = _vm;

        // Idempotent and single-flighted on the shared service, so every AI
        // panel can do this without a thundering herd of status GETs.
        _ = _vm.EnsureStatusLoadedAsync();
    }

    public ICommand? OpenListCommand
    {
        get => (ICommand?)GetValue(OpenListCommandProperty);
        set => SetValue(OpenListCommandProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    private static void OnOpenListCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PoweredTemplatePanel)d)._vm.OpenListCommand = e.NewValue as ICommand;

    private static void OnRefreshCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PoweredTemplatePanel)d)._vm.RefreshCommand = e.NewValue as ICommand;
}
