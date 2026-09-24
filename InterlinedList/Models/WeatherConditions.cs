namespace InterlinedList.Models;

/// <summary>
/// GET /api/weather?latitude=&amp;longitude= — current conditions plus optional
/// forecasts, sourced from the U.S. National Weather Service
/// (<c>api.weather.gov</c>, confirmed by the icon URLs in the extended
/// payload) and <b>cached server-side for 30 minutes</b>. Clients must not
/// poll faster than that cache warrants.
///
/// <para><b>Live-verified 2026-09-16.</b> Query-parameter names come from the
/// live OpenAPI spec: <c>latitude</c>, <c>longitude</c>, <c>extended</c>,
/// <c>refresh</c>. Note these are the <i>long</i> spellings — the sibling
/// bike-share widget endpoint uses <c>lat</c>/<c>lon</c> instead, so the two
/// families genuinely disagree and neither can be guessed from the other.</para>
///
/// <para><b>This endpoint is United States only.</b> Seattle, NYC, Chicago,
/// Anchorage and Honolulu all returned 200; Toronto and Tokyo both returned
/// <c>404 {"error":"Failed to fetch weather data","code":"not_found"}</c>, as
/// did lat/lon 0,0. That is NWS's coverage area showing through the proxy, not
/// a client bug — so a 404 means "no data for this location", which the widget
/// must present as an ordinary empty state rather than an error.</para>
///
/// <para>Without coordinates: <c>400 {"error":"Latitude and longitude are
/// required","code":"bad_request"}</c>. With only one of the two: the same 400.
/// With non-numeric values: <c>400 {"error":"Invalid latitude or
/// longitude","code":"bad_request"}</c>.</para>
///
/// <para>Numeric fields are typed <see cref="double"/> rather than
/// <c>int</c>: every observed value happened to be integral, but this is a
/// third-party feed and NWS reports fractional values elsewhere.
/// <see cref="Humidity"/> was <c>null</c> in all seven observed responses,
/// across five cities — present in the envelope but apparently never
/// populated, so treat a value as a bonus, not an expectation.</para>
/// </summary>
public sealed class WeatherConditions
{
    /// <summary>The NWS station's place name, e.g. "Seattle", "Urban Honolulu".</summary>
    public string? Location { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Human text, e.g. "Sunny", "Scattered Rain Showers".</summary>
    public string? Condition { get; init; }

    /// <summary>
    /// A BoxIcons class name, e.g. "bx-sun" / "bx-cloud-rain" — a *web* icon
    /// font the desktop app does not ship, so it is kept for fidelity but not
    /// rendered. <see cref="Condition"/> is the text the UI shows.
    /// </summary>
    public string? ConditionIcon { get; init; }

    public double? High { get; init; }
    public double? Low { get; init; }

    /// <summary>Always null in every observed response — see the class remarks.</summary>
    public double? Humidity { get; init; }

    public double? WindSpeed { get; init; }

    /// <summary>
    /// IANA zone, e.g. "America/Los_Angeles". Note the casing: weather returns
    /// <c>timeZone</c> while /api/location returns <c>timezone</c>. Harmless
    /// here only because the client's JsonSerializerDefaults.Web options are
    /// case-insensitive.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>Populated only with <c>extended=true</c> (25 rows observed).</summary>
    public List<WeatherHourly> Hourly { get; init; } = new();

    /// <summary>
    /// Populated only with <c>extended=true</c> (14 rows observed — NWS
    /// alternates day and night periods, so 14 rows is ~7 days, not 14).
    /// </summary>
    public List<WeatherPeriod> Weekly { get; init; } = new();
}
