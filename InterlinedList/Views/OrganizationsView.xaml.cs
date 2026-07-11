using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class OrganizationsView : UserControl
{
    public OrganizationsView()
    {
        InitializeComponent();
        var vm = new OrganizationsViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadCommand.ExecuteAsync(null);
    }
}
