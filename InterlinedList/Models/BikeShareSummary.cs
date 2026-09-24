namespace InterlinedList.Models;

/// <summary>
/// Dockless-system totals inside the search radius (the <c>summary</c> object).
/// Seattle/Lime returned bikes 178, scooters 492, nearestKm 0.0151,
/// radiusKm 0.402 on 2026-09-16. Docked systems omit this object.
/// </summary>
public sealed class BikeShareSummary
{
    public int? Bikes { get; init; }
    public int? Scooters { get; init; }
    public double? NearestKm { get; init; }
    public double? RadiusKm { get; init; }
}
