using InterlinedList.Services;
using InterlinedList.ViewModels;
using InterlinedList.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace InterlinedList;

public partial class MainWindow : Window
{
    public NotificationsViewModel Notifications { get; } = new(AppServices.Session);
    public ProfileSummaryViewModel Profile { get; } = new(AppServices.Session);

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, UserControl> _views = new();

    public event EventHandler? LoggedOut;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        Profile.LoggedOut += (_, _) => LoggedOut?.Invoke(this, EventArgs.Empty);

        StartClock();

        _ = Notifications.LoadCommand.ExecuteAsync(null);
        _ = Profile.LoadCommand.ExecuteAsync(null);

        CenterContent.Content = ViewFor("Feed");
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Navigation ────────────────────────────────────────────────────────────
    // "Alerts" toggles the right rail (unchanged since Phase 2); every other
    // nav item swaps the center column via a lazily-created, cached view so
    // switching tabs back and forth doesn't re-fetch each time.

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        if (tag == "Alerts")
        {
            ProfileRail.Visibility = Visibility.Collapsed;
            NotificationsRail.Visibility = Visibility.Visible;
            return;
        }

        NotificationsRail.Visibility = Visibility.Collapsed;
        ProfileRail.Visibility = Visibility.Visible;
        CenterContent.Content = ViewFor(tag);
    }

    private UserControl ViewFor(string tag)
    {
        if (_views.TryGetValue(tag, out var existing)) return existing;

        UserControl view = tag switch
        {
            "Feed" => new FeedView(),
            "Lists" => new ListsView(),
            "Documents" => new DocumentsView(),
            "Organizations" => new OrganizationsView(),
            "Search" => new SearchView(),
            "Accounts" => new ConnectedAccountsView(),
            _ => new FeedView(),
        };

        _views[tag] = view;
        return view;
    }

    // ── Stream clock ──────────────────────────────────────────────────────────

    private void StartClock()
    {
        _clock.Tick += (_, _) =>
            ClockDisplay.Text = DateTime.UtcNow.ToString("HH:mm:ss'Z'");
        _clock.Start();
        ClockDisplay.Text = DateTime.UtcNow.ToString("HH:mm:ss'Z'");
    }
}
