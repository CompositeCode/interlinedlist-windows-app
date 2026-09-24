using System.Globalization;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The five read-only sidebar-widget endpoints. All are bearer-reachable with
/// the standing sync token — none of them need a cookie session.
///
/// <para><b>Every one of these proxies a third party</b> (Hacker News,
/// CoinGecko, a transit feed, GBFS bike-share), so they fail independently of
/// InterlinedList itself and in more than one way: a 502 was observed live from
/// /api/widgets/markets, and /api/widgets/transit answers HTTP 200 while
/// carrying <c>"error":"unavailable"</c> in the body. Callers must treat a
/// widget failure as a cosmetic non-event; see WidgetViewModel, which turns any
/// exception into a quiet placeholder so one flaky upstream can never break the
/// shell.</para>
///
/// <para><b>Query-parameter names were recovered from the live OpenAPI spec and
/// by probe on 2026-09-16, not guessed.</b> Note the two families disagree:
/// bike-share takes <c>lat</c>/<c>lon</c>, while /api/weather and /api/location
/// take <c>latitude</c>/<c>longitude</c>. Transit's required <c>agency</c>
/// parameter is not declared in the spec at all — see
/// <see cref="TransitWidget"/> and <see cref="TransitAgencies"/>.</para>
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Top Hacker News headlines. <paramref name="source"/> is a documented
    /// query parameter but had no observable effect when probed — every value
    /// returned the same default list.
    /// </summary>
    public Task<NewsWidget> GetNewsWidgetAsync(string? source = null, CancellationToken ct = default)
        => GetJsonAsync<NewsWidget>(
            source is { Length: > 0 }
                ? $"api/widgets/news?source={Uri.EscapeDataString(source)}"
                : "api/widgets/news",
            ct);

    /// <summary>
    /// Crypto watchlist quotes. Called bare on purpose: <c>ids</c> expects
    /// CoinGecko ids and a real three-id request 502'd, so the server's own
    /// default watchlist is the reliable path (see <see cref="MarketsWidget"/>).
    /// </summary>
    public Task<MarketsWidget> GetMarketsWidgetAsync(string? ids = null, CancellationToken ct = default)
        => GetJsonAsync<MarketsWidget>(
            ids is { Length: > 0 }
                ? $"api/widgets/markets?ids={Uri.EscapeDataString(ids)}"
                : "api/widgets/markets",
            ct);

    /// <summary>
    /// Normalized transit info for an agency slug. <paramref name="agency"/> is
    /// required — omitting it is a 400 "Unknown agency" — and its vocabulary is
    /// city slugs; use <see cref="TransitAgencies"/> to resolve one from a
    /// profile location rather than hardcoding a string.
    /// </summary>
    public Task<TransitWidget> GetTransitAsync(string agency, CancellationToken ct = default)
        => GetJsonAsync<TransitWidget>(
            $"api/widgets/transit?agency={Uri.EscapeDataString(agency)}", ct);

    /// <summary>
    /// Nearby stops for an agency slug. The coordinates are accepted but made
    /// no difference to the live response, so they are optional here; the
    /// agency is what actually scopes the query.
    /// </summary>
    public Task<TransitWidget> GetTransitStopsAsync(
        string agency, GeoPoint? at = null, CancellationToken ct = default)
    {
        var path = $"api/widgets/transit/stops?agency={Uri.EscapeDataString(agency)}";
        if (at is { } p)
            path += $"&lat={Num(p.Latitude)}&lon={Num(p.Longitude)}";
        return GetJsonAsync<TransitWidget>(path, ct);
    }

    /// <summary>
    /// Bike-share availability around a point. Returns one of two shapes keyed
    /// by <see cref="BikeShareWidget.Kind"/>. With no coordinates the server
    /// answers 200 with an empty docked payload rather than a 400, so
    /// <paramref name="at"/> is nullable and an empty result is a legitimate
    /// "nothing nearby" state.
    ///
    /// <para><paramref name="radiusMeters"/> is plumbed through for
    /// completeness but the server appears to clamp it — 2000 and 8000 both
    /// echoed back <c>radiusMeters: 402</c> — so callers in this app leave it
    /// unset and take the default radius.</para>
    /// </summary>
    public Task<BikeShareWidget> GetBikeShareAsync(
        GeoPoint? at = null, int? radiusMeters = null, CancellationToken ct = default)
    {
        var path = "api/widgets/bike-share";
        var sep = '?';

        if (at is { } p)
        {
            path += $"{sep}lat={Num(p.Latitude)}&lon={Num(p.Longitude)}";
            sep = '&';
        }

        if (radiusMeters is { } r)
            path += $"{sep}radius={r.ToString(CultureInfo.InvariantCulture)}";

        return GetJsonAsync<BikeShareWidget>(path, ct);
    }

    /// <summary>
    /// The coordinates stored on the signed-in user's profile, or null when the
    /// user has not set a location.
    ///
    /// <para>This reads <c>latitude</c>/<c>longitude</c> straight off the
    /// GET /api/user envelope instead of going through the typed
    /// <c>CurrentUser</c> model on purpose: those two properties are being added
    /// to that model in a separate in-flight change, and the widget layer must
    /// not depend on it landing first. Once the typed properties exist this can
    /// collapse into reading <c>CurrentUser</c>.</para>
    /// </summary>
    public async Task<GeoPoint?> GetProfileLocationAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user", ct);

        if (!json.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
            return null;

        return ReadNumber(user, "latitude") is { } lat && ReadNumber(user, "longitude") is { } lon
            ? new GeoPoint(lat, lon)
            : null;
    }

    private static double? ReadNumber(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <summary>
    /// Formats a coordinate for a query string. Invariant culture is
    /// load-bearing: under a comma-decimal locale the default ToString() would
    /// emit "47,6062" and the server would reject it.
    /// </summary>
    private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
