namespace InterlinedList.Models;

/// <summary>
/// One hour of the <c>extended=true</c> forecast. Key union across all 25
/// observed rows — unlike <see cref="WeatherPeriod"/> these carry no
/// name/icon/isDaytime.
/// </summary>
public sealed class WeatherHourly
{
    public DateTimeOffset? StartTime { get; init; }
    public double? Temperature { get; init; }

    /// <summary>Percentage, 0-100.</summary>
    public int? ProbabilityOfPrecipitation { get; init; }

    public string? ShortForecast { get; init; }
}
