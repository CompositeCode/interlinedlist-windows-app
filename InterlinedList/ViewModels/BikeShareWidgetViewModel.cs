using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/widgets/bike-share — nearby bikes, scooters or docks.
///
/// <para>Location-dependent, and explains itself when the profile has no
/// coordinates rather than erroring. Note the endpoint would happily answer
/// 200 with an empty payload in that case, so the guard is about telling the
/// user something useful, not about avoiding a 400.</para>
///
/// <para>Flattens the endpoint's two shapes into one summary line and one list:
/// dockless systems report bike/scooter counts from <c>summary</c>, docked
/// systems report the nearest stations from <c>stations</c> plus totals from
/// <c>within</c>.</para>
/// </summary>
public sealed partial class BikeShareWidgetViewModel : WidgetViewModel
{
    // Availability changes minute to minute but this proxies a GBFS feed.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);

    private const int MaxRows = 3;

    private readonly ProfileLocationProvider _location;

    public BikeShareWidgetViewModel(SessionService session, ProfileLocationProvider location)
        : base(session)
        => _location = location;

    public override string Title => "Bike share";

    protected override TimeSpan CacheFor => Ttl;

    /// <summary>Docked systems only — the nearest stations.</summary>
    public ObservableCollection<BikeShareStationViewModel> Stations { get; } = new();

    /// <summary>The system's name, e.g. "Lime (Seattle)" or "Citi Bike (NYC)".</summary>
    [ObservableProperty]
    private string? systemName;

    /// <summary>One-line availability summary for either system kind.</summary>
    [ObservableProperty]
    private string? availability;

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        Stations.Clear();
        SystemName = null;
        Availability = null;

        if (await _location.GetAsync(ct: ct) is not { } at)
        {
            ShowPlaceholder("Set your location in Settings to see nearby bikes.");
            return;
        }

        var widget = await Session.Api.GetBikeShareAsync(at, ct: ct);

        // A null system means no bike-share network covers this point — a
        // normal outcome, not a failure (lat/lon 0,0 responds the same way).
        if (string.IsNullOrWhiteSpace(widget.SystemName))
        {
            ShowPlaceholder("No bike share near your location.");
            return;
        }

        SystemName = widget.SystemName;

        if (widget.IsDockless)
        {
            Availability = DescribeDockless(widget.Summary);
        }
        else
        {
            Availability = DescribeDocked(widget.Within);
            foreach (var station in widget.Stations.Take(MaxRows))
                Stations.Add(new BikeShareStationViewModel(station));
        }

        if (Availability is null && Stations.Count == 0)
            ShowPlaceholder("Nothing available nearby.");
        else
            ShowContent();
    }

    private static string? DescribeDockless(BikeShareSummary? summary)
    {
        if (summary is null) return null;

        var parts = new List<string>(2);
        if (summary.Bikes is > 0) parts.Add($"{summary.Bikes} bikes");
        if (summary.Scooters is > 0) parts.Add($"{summary.Scooters} scooters");

        if (parts.Count == 0) return null;

        var text = string.Join(" · ", parts);
        if (summary.NearestKm is { } km)
            text += $" · nearest {FormatDistance(km)}";

        return text;
    }

    private static string? DescribeDocked(BikeShareWithin? within)
    {
        if (within is null) return null;

        var bikes = within.Bikes ?? 0;
        var docks = within.Docks ?? 0;
        if (bikes == 0 && docks == 0) return null;

        return $"{bikes} bikes · {docks} docks · {within.Stations ?? 0} stations";
    }

    internal static string FormatDistance(double km)
        => km < 1
            ? $"{Math.Round(km * 1000)} m"
            : km.ToString("0.0", CultureInfo.CurrentCulture) + " km";
}
