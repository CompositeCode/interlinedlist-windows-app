using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Resolves the undocumented <c>agency</c> query parameter that
/// GET /api/widgets/transit and /transit/stops both require.
///
/// <para><b>Why this table exists.</b> The OpenAPI spec declares no parameters
/// for either path, but a bare call is a 400 "Unknown agency". Probing live on
/// 2026-09-16 established that (a) the parameter is named <c>agency</c> — none
/// of agencyId/agency_id/system/provider/feed/region/city/id/operator/network/
/// source/org are read — and (b) its vocabulary is *city slugs*, not transit
/// operator names: <c>seattle</c> and <c>portland</c> answered 200, while
/// sound-transit, king-county-metro, nyc, chicago, boston, san-francisco,
/// washington, los-angeles, philadelphia, atlanta, denver, austin, miami,
/// toronto, london, bart, caltrain and trimet were all 400.</para>
///
/// <para><b>This mapping is deliberately partial.</b> It holds only slugs
/// proven to be accepted, so a user outside those metros gets an honest
/// "not available for your location" message instead of a 400 dressed up as an
/// error. Add a row only after confirming the slug returns 200 live.</para>
/// </summary>
public static class TransitAgencies
{
    /// <summary>An agency slug the API accepts, anchored at its metro centre.</summary>
    private sealed record Agency(string Slug, string Label, double Latitude, double Longitude);

    // Only live-verified-accepted slugs belong here.
    private static readonly Agency[] Known =
    {
        new("seattle",  "Seattle",  47.6062,  -122.3321),
        new("portland", "Portland", 45.5152,  -122.6784),
    };

    /// <summary>
    /// How far from a metro centre the mapping still claims coverage. Generous
    /// enough for a metro area, tight enough that an unrelated city does not
    /// silently borrow its neighbour's feed.
    /// </summary>
    private const double CoverageRadiusKm = 80;

    /// <summary>
    /// The agency slug covering <paramref name="at"/>, or null when no known
    /// agency is near enough. Null is a normal outcome, not an error — the
    /// caller should explain that transit is unavailable for this location.
    /// </summary>
    public static string? ResolveSlug(GeoPoint at) => Resolve(at)?.Slug;

    /// <summary>The human label for the covering agency, or null.</summary>
    public static string? ResolveLabel(GeoPoint at) => Resolve(at)?.Label;

    private static Agency? Resolve(GeoPoint at)
    {
        Agency? best = null;
        var bestKm = double.MaxValue;

        foreach (var agency in Known)
        {
            var km = DistanceKm(at.Latitude, at.Longitude, agency.Latitude, agency.Longitude);
            if (km < bestKm)
            {
                bestKm = km;
                best = agency;
            }
        }

        return bestKm <= CoverageRadiusKm ? best : null;
    }

    /// <summary>Great-circle distance in kilometres (haversine).</summary>
    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0;

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
