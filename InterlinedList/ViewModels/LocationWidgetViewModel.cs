using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/location — city, state and timezone for the user's stored profile
/// location.
///
/// <para>A reverse geocode of a fixed coordinate pair does not change, so this
/// caches for hours rather than minutes; the only thing that moves it is the
/// user editing their profile location, and the rail's refresh-all re-reads
/// that first.</para>
///
/// <para>Also shows the local time in the resolved zone — the one genuinely
/// live part of this card, and the reason the rail's UTC stream clock is not
/// the whole story for a user working across zones.</para>
/// </summary>
public sealed partial class LocationWidgetViewModel : WidgetViewModel
{
    // A geocode of a fixed point is effectively static.
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    private readonly ProfileLocationProvider _location;

    public LocationWidgetViewModel(SessionService session, ProfileLocationProvider location)
        : base(session)
        => _location = location;

    public override string Title => "Location";

    protected override TimeSpan CacheFor => Ttl;

    /// <summary>e.g. "Seattle, WA".</summary>
    [ObservableProperty]
    private string? place;

    /// <summary>e.g. "United States".</summary>
    [ObservableProperty]
    private string? country;

    /// <summary>IANA zone plus the current local time there, when resolvable.</summary>
    [ObservableProperty]
    private string? timezone;

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        Place = null;
        Country = null;
        Timezone = null;

        if (await _location.GetAsync(ct: ct) is not { } at)
        {
            ShowPlaceholder("Set your location in Settings to see it here.");
            return;
        }

        Models.LocationInfo info;
        try
        {
            info = await Session.Api.GetLocationAsync(at, ct);
        }
        catch (InterlinedApiException ex) when (ex.StatusCode == 404)
        {
            // The geocoder covers the United States only; an unresolvable point
            // is an empty state, not a failure.
            ShowPlaceholder("Your location could not be resolved.");
            return;
        }

        Place = Join(info.City, info.State);
        Country = info.Country;
        Timezone = DescribeZone(info.Timezone);

        if (string.IsNullOrWhiteSpace(Place) && string.IsNullOrWhiteSpace(Country))
            ShowPlaceholder("Your location could not be resolved.");
        else
            ShowContent();
    }

    private static string? Join(string? city, string? state)
    {
        var hasCity = !string.IsNullOrWhiteSpace(city);
        var hasState = !string.IsNullOrWhiteSpace(state);

        return (hasCity, hasState) switch
        {
            (true, true) => $"{city}, {state}",
            (true, false) => city,
            (false, true) => state,
            _ => null,
        };
    }

    /// <summary>
    /// "America/Los_Angeles · 13:42" when Windows can resolve the IANA id, and
    /// just the id when it cannot. .NET on Windows understands IANA ids from
    /// .NET 6 onward, but a machine with an outdated ICU/registry can still
    /// miss one — a widget must not throw over a timezone name.
    /// </summary>
    private static string? DescribeZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return null;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
            return $"{ianaId} · {local:HH:mm}";
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return ianaId;
        }
    }
}
