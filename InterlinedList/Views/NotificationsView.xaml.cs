using System.Windows.Controls;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The full notification history — the app's equivalent of the web's
/// Notifications page, reached from "See all" in the Alerts rail.
/// </summary>
/// <remarks>
/// Unlike the other center views this does NOT new up its own ViewModel: the
/// Alerts rail and this view must agree on read state and the unread count, so
/// the shell hands both the same <see cref="NotificationsViewModel"/> instance.
/// </remarks>
public partial class NotificationsView : UserControl
{
    private readonly NotificationsViewModel _vm;

    public NotificationsView(NotificationsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;
    }

    /// <summary>Re-read the feed. Called by the shell each time the view is shown.</summary>
    public void Refresh() => _ = _vm.LoadFeedCommand.ExecuteAsync(null);
}
