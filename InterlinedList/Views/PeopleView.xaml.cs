using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class PeopleView : UserControl
{
    private readonly ProfileViewModel _vm;

    public PeopleView()
    {
        InitializeComponent();
        _vm = new ProfileViewModel(Services.AppServices.Session);
        DataContext = _vm;
        _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Open a specific user's profile (used by shell navigation from a feed/search card).</summary>
    public void LoadProfile(string username)
    {
        _vm.LookupUsername = username;
        _ = _vm.LoadProfileCommand.ExecuteAsync(null);
    }
}
