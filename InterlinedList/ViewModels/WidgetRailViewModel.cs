using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Owns the right rail's widget stack. Each child is independent: they load
/// concurrently, cache on their own clock, refresh on their own button, and —
/// because <see cref="WidgetViewModel.LoadAsync"/> never throws — one failing
/// upstream cannot stop the others or the shell.
/// </summary>
public sealed partial class WidgetRailViewModel : ObservableObject
{
    private readonly ProfileLocationProvider _location;

    public WidgetRailViewModel(SessionService session)
    {
        _location = new ProfileLocationProvider(session);

        News = new NewsWidgetViewModel(session);
        Markets = new MarketsWidgetViewModel(session);
        Transit = new TransitWidgetViewModel(session, _location);
        BikeShare = new BikeShareWidgetViewModel(session, _location);

        Widgets = new WidgetViewModel[] { News, Markets, Transit, BikeShare };
    }

    public NewsWidgetViewModel News { get; }
    public MarketsWidgetViewModel Markets { get; }
    public TransitWidgetViewModel Transit { get; }
    public BikeShareWidgetViewModel BikeShare { get; }

    /// <summary>Every card, in rail order — used for load-all / refresh-all.</summary>
    public IReadOnlyList<WidgetViewModel> Widgets { get; }

    /// <summary>
    /// Loads every widget concurrently. Safe to call repeatedly: each child
    /// honours its own cache window, so a nav switch or a re-entrant call is a
    /// no-op rather than another round of requests.
    /// </summary>
    [RelayCommand]
    private Task LoadAllAsync(CancellationToken ct)
        => Task.WhenAll(Widgets.Select(w => w.LoadAsync(force: false, ct)));

    /// <summary>
    /// Refresh-all, bypassing the caches. Re-reads the profile location first
    /// so a location just set in Settings takes effect without a restart.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync(CancellationToken ct)
    {
        try
        {
            await _location.GetAsync(force: true, ct);
        }
        catch (Exception ex)
        {
            // Same rule as the widgets themselves: never let the rail throw.
            // The children will fall back to their own placeholders.
            AppLog.Warn($"Widget rail could not re-read the profile location: {ex.Message}");
        }

        await Task.WhenAll(Widgets.Select(w => w.LoadAsync(force: true, ct)));
    }
}
