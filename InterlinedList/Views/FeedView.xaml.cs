using InterlinedList.Services;
using InterlinedList.ViewModels;
using System.Windows.Controls;

namespace InterlinedList.Views;

public partial class FeedView : UserControl
{
    public FeedViewModel ViewModel { get; }

    public FeedView()
    {
        InitializeComponent();

        ViewModel = new FeedViewModel(AppServices.Session);
        DataContext = ViewModel;

        _ = ViewModel.RefreshCommand.ExecuteAsync(null);
        _ = ViewModel.LoadCrossPostOptionsCommand.ExecuteAsync(null);
    }
}
