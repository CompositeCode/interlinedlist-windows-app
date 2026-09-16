using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The Powered Document surface — all four <c>context.mode</c> variants in one
/// self-contained card.
///
/// <b>Hosting it costs one XAML line and no code-behind:</b>
/// <code>
/// &lt;local:PoweredDocumentPanel OpenDocumentCommand="{Binding SelectDocumentCommand}"
///                            RefreshCommand="{Binding LoadCommand}"/&gt;
/// </code>
///
/// <c>Views/DocumentsView.xaml</c> is contended by #139, so this follows the
/// #55/#56 pattern for the same reason #14 does: the host contributes a single
/// line it can move anywhere inside a <c>StackPanel</c> bound to
/// <c>DocumentsViewModel</c>, and everything else lives here.
///
/// The control fetches the user's own lists and documents itself (for the
/// <c>from_list</c> / <c>from_article</c> pickers) rather than taking them from
/// the host, so it doesn't depend on the host's load order or on collections
/// another PR might rename.
/// </summary>
public partial class PoweredDocumentPanel : UserControl
{
    /// <summary>
    /// Invoked with the freshly re-fetched <see cref="Models.DocumentSummary"/>
    /// after a confirmed generation, so the host can open it in its editor.
    /// </summary>
    public static readonly DependencyProperty OpenDocumentCommandProperty =
        DependencyProperty.Register(
            nameof(OpenDocumentCommand), typeof(ICommand), typeof(PoweredDocumentPanel),
            new PropertyMetadata(null, OnOpenDocumentCommandChanged));

    /// <summary>Invoked with null after a confirmed generation so the host reloads its lists.</summary>
    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.Register(
            nameof(RefreshCommand), typeof(ICommand), typeof(PoweredDocumentPanel),
            new PropertyMetadata(null, OnRefreshCommandChanged));

    private readonly PoweredDocumentPanelViewModel _vm;

    public PoweredDocumentPanel()
    {
        InitializeComponent();
        _vm = new PoweredDocumentPanelViewModel(AppServices.Session);
        DataContext = _vm;

        _ = _vm.EnsureStatusLoadedAsync();
    }

    public ICommand? OpenDocumentCommand
    {
        get => (ICommand?)GetValue(OpenDocumentCommandProperty);
        set => SetValue(OpenDocumentCommandProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    private static void OnOpenDocumentCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PoweredDocumentPanel)d)._vm.OpenDocumentCommand = e.NewValue as ICommand;

    private static void OnRefreshCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PoweredDocumentPanel)d)._vm.RefreshCommand = e.NewValue as ICommand;
}
