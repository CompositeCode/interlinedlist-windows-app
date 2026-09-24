using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/widgets/transit/stops — nearby stops.
///
/// <para>Location-dependent in two ways, and it explains itself in both rather
/// than erroring: with no profile location it points the user at Settings, and
/// with a location outside the API's partial agency vocabulary (see
/// <see cref="TransitAgencies"/>) it says transit isn't covered there. Neither
/// case issues a request that would come back 400 "Unknown agency".</para>
///
/// <para>The endpoint can also answer HTTP 200 with
/// <c>"error":"unavailable"</c> when its own upstream feed is down — which is
/// exactly what it did for every accepted agency when probed on 2026-09-16 —
/// so success is judged by the body, not the status code.</para>
/// </summary>
public sealed partial class TransitWidgetViewModel : WidgetViewModel
{
    // Departures are time-sensitive, but this is a proxied feed; one minute.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private const int MaxRows = 5;

    private readonly ProfileLocationProvider _location;

    public TransitWidgetViewModel(SessionService session, ProfileLocationProvider location)
        : base(session)
        => _location = location;

    public override string Title => "Transit";

    protected override TimeSpan CacheFor => Ttl;

    public ObservableCollection<TransitStop> Stops { get; } = new();

    /// <summary>The resolved agency label, shown as the card's subtitle.</summary>
    [ObservableProperty]
    private string? agencyLabel;

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        Stops.Clear();
        AgencyLabel = null;

        if (await _location.GetAsync(ct: ct) is not { } at)
        {
            ShowPlaceholder("Set your location in Settings to see nearby stops.");
            return;
        }

        if (TransitAgencies.ResolveSlug(at) is not { } agency)
        {
            ShowPlaceholder("Transit isn't available for your location yet.");
            return;
        }

        AgencyLabel = TransitAgencies.ResolveLabel(at);

        var widget = await Session.Api.GetTransitStopsAsync(agency, at, ct);

        // A populated `error` with a 200 status means the server's upstream
        // transit feed failed — degrade quietly, same as an exception would.
        if (!string.IsNullOrWhiteSpace(widget.Error))
        {
            ShowPlaceholder("Transit data is unavailable right now.");
            return;
        }

        foreach (var stop in widget.Stops.Take(MaxRows))
            Stops.Add(stop);

        if (Stops.Count == 0)
            ShowPlaceholder("No stops nearby.");
        else
            ShowContent();
    }
}
