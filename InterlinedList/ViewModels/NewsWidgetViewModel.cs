using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/widgets/news — top Hacker News headlines. Clicking one opens it in
/// the OS browser; the app does not embed a web view.
/// </summary>
public sealed partial class NewsWidgetViewModel : WidgetViewModel
{
    // Headlines move slowly and this is a third-party proxy, so ten minutes.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private const int MaxRows = 6;

    public NewsWidgetViewModel(SessionService session) : base(session) { }

    public override string Title => "Top news";

    protected override TimeSpan CacheFor => Ttl;

    public ObservableCollection<NewsItem> Items { get; } = new();

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        var widget = await Session.Api.GetNewsWidgetAsync(ct: ct);

        Items.Clear();
        foreach (var item in widget.Items.Where(i => !string.IsNullOrWhiteSpace(i.Title)).Take(MaxRows))
            Items.Add(item);

        if (Items.Count == 0)
            ShowPlaceholder("No headlines right now.");
        else
            ShowContent();
    }

    [RelayCommand]
    private void OpenLink(NewsItem? item)
    {
        if (item?.Url is not { Length: > 0 } url) return;

        // Only http(s) — a headline URL comes from a third party, so never hand
        // an arbitrary scheme (file:, ms-settings:, a local executable path) to
        // the shell.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        try
        {
            // UseShellExecute = true is required for .NET Core+ to hand the URL
            // to the default browser (same as OpenProviderAuthorize).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not open news link: {ex.Message}");
        }
    }
}
