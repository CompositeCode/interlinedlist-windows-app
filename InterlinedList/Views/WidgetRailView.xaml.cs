using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The right rail's widget stack. Self-contained in the repo's usual way: it
/// news up its own ViewModel from <see cref="Services.AppServices.Session"/>
/// and kicks off the initial load itself, so hosting it costs MainWindow one
/// element and no code-behind.
/// </summary>
public partial class WidgetRailView : UserControl
{
    public WidgetRailView()
    {
        InitializeComponent();

        var vm = new WidgetRailViewModel(Services.AppServices.Session);
        DataContext = vm;

        // Fire-and-forget: the widgets load concurrently off the dispatcher and
        // each one swallows its own failures, so nothing here can block the
        // shell or throw into the constructor.
        _ = vm.LoadAllCommand.ExecuteAsync(null);
    }
}
