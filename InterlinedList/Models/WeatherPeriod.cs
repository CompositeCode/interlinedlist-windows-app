namespace InterlinedList.Models;

/// <summary>
/// One named period of the <c>extended=true</c> weekly forecast, e.g.
/// "This Afternoon", "Tonight". Key union across all 14 observed rows.
/// </summary>
public sealed class WeatherPeriod
{
    /// <summary>NWS period label, e.g. "This Afternoon", "Tonight", "Thursday".</summary>
    public string? Name { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    /// <summary>NWS alternates day and night periods; this distinguishes them.</summary>
    public bool? IsDaytime { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Percentage, 0-100.</summary>
    public int? ProbabilityOfPrecipitation { get; init; }

    public string? ShortForecast { get; init; }

    /// <summary>
    /// An absolute <c>api.weather.gov</c> icon URL. Not rendered — the rail
    /// shows text — but retained because it is the clearest evidence of the
    /// upstream provider.
    /// </summary>
    public string? Icon { get; init; }
}
