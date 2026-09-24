using System.Windows.Controls;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// Standing-sync-token management, grouped by device label, with bulk revoke.
/// </summary>
/// <remarks>
/// A self-contained control rather than markup inside <c>SettingsView</c>, so it
/// can be hosted with a one-line add once the in-flight Settings work lands.
/// Follows the repo's convention of a view owning its own ViewModel.
/// </remarks>
public partial class SessionsPanel : UserControl
{
    private readonly SessionsPanelViewModel _vm;

    public SessionsPanel()
    {
        InitializeComponent();
        _vm = new SessionsPanelViewModel(AppServices.Session);
        DataContext = _vm;
    }

    /// <summary>Load (or reload) the session list. Call when the host view is shown.</summary>
    public void Refresh() => _ = _vm.LoadCommand.ExecuteAsync(null);
}
