using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class DirectMessagesView : UserControl
{
    public DirectMessagesView()
    {
        InitializeComponent();
        var vm = new DirectMessagesViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadCommand.ExecuteAsync(null);
    }
}
