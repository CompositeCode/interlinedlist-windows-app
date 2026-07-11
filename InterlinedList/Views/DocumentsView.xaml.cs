using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
        var vm = new DocumentsViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadCommand.ExecuteAsync(null);
    }
}
