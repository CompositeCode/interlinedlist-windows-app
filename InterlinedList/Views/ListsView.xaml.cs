using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class ListsView : UserControl
{
    public ListsView()
    {
        InitializeComponent();
        var vm = new ListsViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadListsCommand.ExecuteAsync(null);
    }
}
