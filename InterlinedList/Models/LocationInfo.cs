namespace InterlinedList.Models;

/// <summary>
/// GET /api/location?latitude=&amp;longitude= — reverse geocode of a coordinate
/// pair into city, state, country and timezone.
///
/// <para><b>Live-verified 2026-09-16:</b>
/// <c>200 {"city":"Seattle","state":"WA","country":"United States",
/// "coordinates":{"latitude":47.6062,"longitude":-122.3321},
/// "timezone":"America/Los_Angeles"}</c>. Query-parameter names are
/// <c>latitude</c>/<c>longitude</c> per the live OpenAPI spec — the long
/// spellings, matching /api/weather and unlike bike-share's
/// <c>lat</c>/<c>lon</c>.</para>
///
/// <para><b>United States only</b>, same coverage as /api/weather: Seattle,
/// NYC, Chicago, Anchorage and Honolulu returned 200, while Toronto, Tokyo,
/// lat/lon 0,0 and out-of-range coordinates all returned
/// <c>404 {"error":"Failed to fetch location data","code":"not_found"}</c>.
/// A 404 therefore means "cannot resolve this point", an ordinary empty state
/// rather than an error.</para>
///
/// <para>Missing or non-numeric coordinates give
/// <c>400 {"error":"Latitude and longitude are required","code":"bad_request"}</c>
/// (supplying only one of the pair counts as missing).</para>
///
/// <para>Note the key is <c>timezone</c> here but <c>timeZone</c> on
/// /api/weather — an upstream inconsistency the client absorbs only because
/// its JsonSerializerDefaults.Web options are case-insensitive.</para>
/// </summary>
public sealed class LocationInfo
{
    /// <summary>e.g. "Seattle", "Urban Honolulu" — the geocoder's place name.</summary>
    public string? City { get; init; }

    /// <summary>Two-letter state/territory code, e.g. "WA", "HI".</summary>
    public string? State { get; init; }

    public string? Country { get; init; }

    /// <summary>Echo of the requested coordinates.</summary>
    public GeoCoordinates? Coordinates { get; init; }

    /// <summary>IANA zone, e.g. "America/Los_Angeles", "Pacific/Honolulu".</summary>
    public string? Timezone { get; init; }
}
