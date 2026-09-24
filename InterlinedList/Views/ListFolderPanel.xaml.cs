using System.Windows.Controls;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// List-folder tree with create / rename / move / delete.
/// </summary>
/// <remarks>
/// Self-contained so it can be hosted with one line — <c>ListsView</c> is being
/// reworked across the #161/#163/#164 stack.
/// </remarks>
public partial class ListFolderPanel : UserControl
{
    private readonly ListFolderPanelViewModel _vm;

    public ListFolderPanel()
    {
        InitializeComponent();
        _vm = new ListFolderPanelViewModel(AppServices.Session);
        DataContext = _vm;
    }

    /// <summary>Load (or reload) the folder tree.</summary>
    public void Refresh() => _ = _vm.LoadCommand.ExecuteAsync(null);
}
