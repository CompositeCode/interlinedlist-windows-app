namespace InterlinedList.Models;

/// <summary>
/// One docked bike-share station. Key union across the NYC, Chicago and SF
/// responses (three stations each); dockless systems omit this array entirely.
/// </summary>
public sealed class BikeShareStation
{
    public string? Name { get; init; }
    public int? Bikes { get; init; }
    public int? Docks { get; init; }
    public double? DistanceKm { get; init; }
}
