using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// GET /api/weather and GET /api/location — both bearer-reachable, both
/// read-only, and both needing nothing but a coordinate pair.
///
/// <para><b>Coordinates are explicit parameters on purpose.</b> They are not
/// read from a <c>CurrentUser</c> field inside these methods: that keeps the
/// service independently testable, and it decouples this from the in-flight
/// change adding <c>Latitude</c>/<c>Longitude</c> to that model. The UI layer
/// sources them from the profile via <see cref="ProfileLocationProvider"/>.</para>
///
/// <para><b>Parameter names are the long spellings</b> —
/// <c>latitude</c>/<c>longitude</c>, from the live OpenAPI spec — which differs
/// from the bike-share widget's <c>lat</c>/<c>lon</c>. Do not normalise the two
/// families onto one spelling without re-probing; they really are different.</para>
///
/// <para><b>Both endpoints are United States only</b> (the National Weather
/// Service is the upstream). A point outside coverage returns 404, not 200 with
/// empty data — see <see cref="WeatherConditions"/> — so callers should treat
/// 404 as "no data for this location" rather than a failure worth shouting
/// about.</para>
///
/// <para><b>Weather is cached server-side for 30 minutes</b>, so there is no
/// value in polling faster; the widget layer's cache window matches.</para>
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Current conditions for a point.
    /// </summary>
    /// <param name="at">
    /// The coordinates to report on — in this app, always the user's stored
    /// profile location. The app never asks the OS for a position: the web
    /// product's geolocation permission prompt has no desktop equivalent.
    /// </param>
    /// <param name="extended">
    /// Adds the <c>hourly</c> (25 rows) and <c>weekly</c> (14 day/night
    /// periods) forecast arrays. Roughly 37× the payload — 6178 bytes versus
    /// 166 — so leave it off for a one-line rail card.
    /// </param>
    /// <param name="refresh">
    /// Asks the server to bypass its own 30-minute cache. Accepted (200), but
    /// use it only for an explicit user-initiated refresh; the whole point of
    /// that cache is to shield the NWS upstream.
    /// </param>
    public Task<WeatherConditions> GetWeatherAsync(
        GeoPoint at, bool extended = false, bool refresh = false, CancellationToken ct = default)
    {
        var path = $"api/weather?latitude={Num(at.Latitude)}&longitude={Num(at.Longitude)}";
        if (extended) path += "&extended=true";
        if (refresh) path += "&refresh=true";

        return GetJsonAsync<WeatherConditions>(path, ct);
    }

    /// <summary>
    /// Reverse-geocodes a point to city / state / country / timezone. Same
    /// coverage and same 404-means-unresolvable behaviour as
    /// <see cref="GetWeatherAsync"/>.
    /// </summary>
    public Task<LocationInfo> GetLocationAsync(GeoPoint at, CancellationToken ct = default)
        => GetJsonAsync<LocationInfo>(
            $"api/location?latitude={Num(at.Latitude)}&longitude={Num(at.Longitude)}", ct);
}
