using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class ConnectedAccountsView : UserControl
{
    public ConnectedAccountsView()
    {
        InitializeComponent();
        var vm = new ConnectedAccountsViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadCommand.ExecuteAsync(null);
    }
}
