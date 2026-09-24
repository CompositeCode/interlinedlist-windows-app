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

        // Feed/search cards raise this to open a user's profile in the People tab.
        Navigator.OnOpenProfile = OpenProfile;
        // Account deletion (Settings) routes back to the login screen through here.
        Navigator.OnLoggedOut = () => LoggedOut?.Invoke(this, EventArgs.Empty);

        // Notification click-through. The server hands us a structured target
        // ({messageId, listId, orgId}), so each kind gets a real destination.
        Navigator.OnOpenMessage = OpenMessage;
        Navigator.OnOpenList = _ => ShowCenter("Lists");
        Navigator.OnOpenOrganization = _ => ShowCenter("Organizations");
        Navigator.OnOpenConnectedAccounts = () => ShowCenter("Accounts");

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

    // Navigator.OnOpenProfile → switch to the People tab and load the given user.
    private void OpenProfile(string username)
    {
        NotificationsRail.Visibility = Visibility.Collapsed;
        ProfileRail.Visibility = Visibility.Visible;
        var view = (Views.PeopleView)ViewFor("People");
        CenterContent.Content = view;
        view.LoadProfile(username);
    }

    /// <summary>Swap the center column to a tab, as a nav click would.</summary>
    private void ShowCenter(string tag)
    {
        NotificationsRail.Visibility = Visibility.Collapsed;
        ProfileRail.Visibility = Visibility.Visible;
        CenterContent.Content = ViewFor(tag);
    }

    // Notification → a specific message. The Feed is where messages live; a
    // dedicated single-message thread view is #35, so for now this opens the
    // Feed rather than pretending to deep-link. Deliberately not parsing the
    // server's `routePath`, which is a web URL.
    private void OpenMessage(string messageId) => ShowCenter("Feed");

    /// <summary>"See all" in the Alerts rail → the full notification history.</summary>
    private void ShowAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        NotificationsRail.Visibility = Visibility.Collapsed;
        ProfileRail.Visibility = Visibility.Visible;
        var view = (Views.NotificationsView)ViewFor("Notifications");
        CenterContent.Content = view;
        view.Refresh();
    }

    private UserControl ViewFor(string tag)
    {
        if (_views.TryGetValue(tag, out var existing)) return existing;

        UserControl view = tag switch
        {
            "Feed" => new FeedView(),
            "Messages" => new DirectMessagesView(),
            "Lists" => new ListsView(),
            "Documents" => new DocumentsView(),
            "Organizations" => new OrganizationsView(),
            "People" => new PeopleView(),
            "Search" => new SearchView(),
            "Accounts" => new ConnectedAccountsView(),
            "Settings" => new SettingsView(),
            // Shares the shell's NotificationsViewModel with the Alerts rail so
            // read state and the unread count can't diverge between the two.
            "Notifications" => new NotificationsView(Notifications),
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
