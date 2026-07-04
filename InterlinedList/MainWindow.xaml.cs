using InterlinedList.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace InterlinedList;

public partial class MainWindow : Window
{
    public ObservableCollection<Post> Posts { get; } = [];
    public ObservableCollection<string> TrendingTags { get; } =
    [
        "#signals", "#breakingnews", "#tech", "#markets", "#streams", "#opensource"
    ];

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        LoadSamplePosts();
        StartClock();
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder: phase 2 will wire view switching
    }

    // ── Stream clock ──────────────────────────────────────────────────────────

    private void StartClock()
    {
        _clock.Tick += (_, _) =>
            ClockDisplay.Text = DateTime.UtcNow.ToString("HH:mm:ss'Z'");
        _clock.Start();
        ClockDisplay.Text = DateTime.UtcNow.ToString("HH:mm:ss'Z'");
    }

    // ── Sample data ───────────────────────────────────────────────────────────

    private void LoadSamplePosts()
    {
        var now = DateTime.UtcNow;
        Posts.Add(new Post
        {
            Id = 1, Stream = StreamColor.Amber, StreamName = "Breaking",
            Author = "newswatcher", Timestamp = now.AddMinutes(-3),
            Body = "Markets update: indices showing mixed signals in afternoon trading. Volume elevated on tech names.",
            Digs = 14, IsLive = true
        });
        Posts.Add(new Post
        {
            Id = 2, Stream = StreamColor.Teal, StreamName = "Tech Signals",
            Author = "adron", Timestamp = now.AddMinutes(-12),
            Body = ".NET 10 ships with a major WPF refresh — better per-monitor DPI scaling and trimmed startup time across the board.",
            Digs = 52
        });
        Posts.Add(new Post
        {
            Id = 3, Stream = StreamColor.Green, StreamName = "Community",
            Author = "streamkeeper", Timestamp = now.AddMinutes(-28),
            Body = "Office hours reminder: stream moderation Q&A is today at 1800Z. Bring questions about stream weighting and archive policy.",
            Digs = 31
        });
        Posts.Add(new Post
        {
            Id = 4, Stream = StreamColor.Teal, StreamName = "Infrastructure",
            Author = "ops-relay", Timestamp = now.AddMinutes(-47),
            Body = "Relay cluster rotated successfully. Read latency p99 down to 18ms. All shards green.",
            Digs = 9
        });
        Posts.Add(new Post
        {
            Id = 5, Stream = StreamColor.Amber, StreamName = "Highlights",
            Author = "curator", Timestamp = now.AddHours(-1).AddMinutes(-5),
            Body = "Top thread of the week: the deep-dive on signal decay and why older posts shouldn't disappear — they should just sink gracefully. 200+ replies.",
            Digs = 88
        });
        Posts.Add(new Post
        {
            Id = 6, Stream = StreamColor.Green, StreamName = "Open Source",
            Author = "codepath", Timestamp = now.AddHours(-2),
            Body = "InterlinedList core is heading toward open sourcing the relay protocol spec. Draft RFC posted to the dev stream.",
            Digs = 67
        });
    }
}
