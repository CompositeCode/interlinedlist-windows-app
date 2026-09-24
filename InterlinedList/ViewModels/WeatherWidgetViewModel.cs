using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/weather — current conditions for the user's stored profile
/// location.
///
/// <para><b>Polls no faster than the server's own cache.</b> Weather is cached
/// server-side for 30 minutes, so <see cref="CacheFor"/> matches that exactly:
/// a nav switch, a collapse/expand, or a repeated load-all inside the window
/// costs nothing. The card's explicit refresh button bypasses the client cache
/// but deliberately does <i>not</i> send <c>refresh=true</c> — a user tapping
/// refresh should not punch through to the NWS upstream.</para>
///
/// <para>Explains itself in both empty cases: no profile coordinates points at
/// Settings, and a 404 (the endpoint covers the United States only) reports no
/// data for the location rather than an error.</para>
/// </summary>
public sealed partial class WeatherWidgetViewModel : WidgetViewModel
{
    /// <summary>Matches the documented 30-minute server-side cache.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly ProfileLocationProvider _location;

    public WeatherWidgetViewModel(SessionService session, ProfileLocationProvider location)
        : base(session)
        => _location = location;

    public override string Title => "Weather";

    protected override TimeSpan CacheFor => Ttl;

    /// <summary>The NWS place name, shown as the card's subtitle.</summary>
    [ObservableProperty]
    private string? place;

    /// <summary>Current temperature, e.g. "72°".</summary>
    [ObservableProperty]
    private string? temperature;

    /// <summary>Condition text, e.g. "Scattered Rain Showers".</summary>
    [ObservableProperty]
    private string? condition;

    /// <summary>High/low and wind, e.g. "H 72° · L 55° · 8 mph".</summary>
    [ObservableProperty]
    private string? detail;

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        Place = null;
        Temperature = null;
        Condition = null;
        Detail = null;

        if (await _location.GetAsync(ct: ct) is not { } at)
        {
            ShowPlaceholder("Set your location in Settings to see the weather.");
            return;
        }

        Models.WeatherConditions weather;
        try
        {
            // extended=false: the rail shows one line, and the extended payload
            // is ~37x larger (6178 vs 166 bytes) for forecasts nothing renders.
            weather = await Session.Api.GetWeatherAsync(at, ct: ct);
        }
        catch (InterlinedApiException ex) when (ex.StatusCode == 404)
        {
            // Outside National Weather Service coverage — an ordinary empty
            // state, not a failure. Anything else falls through to the base
            // class's catch-all.
            ShowPlaceholder("No weather data for your location.");
            return;
        }

        Place = weather.Location;
        Temperature = Degrees(weather.Temperature);
        Condition = weather.Condition;
        Detail = BuildDetail(weather);

        if (Temperature is null && string.IsNullOrWhiteSpace(Condition))
            ShowPlaceholder("No weather data for your location.");
        else
            ShowContent();
    }

    private static string? BuildDetail(Models.WeatherConditions weather)
    {
        var parts = new List<string>(4);

        if (Degrees(weather.High) is { } high) parts.Add($"H {high}");
        if (Degrees(weather.Low) is { } low) parts.Add($"L {low}");

        if (weather.WindSpeed is { } wind)
            parts.Add(wind.ToString("0.#", CultureInfo.CurrentCulture) + " mph");

        // Humidity was null in every observed response, but render it if it
        // ever shows up rather than silently dropping it.
        if (weather.Humidity is { } humidity)
            parts.Add(humidity.ToString("0.#", CultureInfo.CurrentCulture) + "% RH");

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// The API reports Fahrenheit (it proxies the U.S. National Weather
    /// Service) and does not send a unit, so the degree sign is shown bare
    /// rather than claiming a scale the payload never states.
    /// </summary>
    private static string? Degrees(double? value)
        => value is null ? null : value.Value.ToString("0.#", CultureInfo.CurrentCulture) + "°";
}
