namespace InterlinedList.Models;

/// <summary>
/// One map pin from GET /api/widgets/bike-share. Key union across both system
/// kinds: dockless points (Seattle, 300 of them) carry only kind/lat/lon, while
/// docked points (NYC/Chicago/SF) additionally carry name/bikes/docks — hence
/// everything past the coordinates is nullable.
/// </summary>
public sealed class BikeSharePoint
{
    /// <summary>Vehicle or station type, e.g. "bike", "scooter", "station".</summary>
    public string? Kind { get; init; }

    public double? Lat { get; init; }
    public double? Lon { get; init; }

    // Docked systems only.
    public string? Name { get; init; }
    public int? Bikes { get; init; }
    public int? Docks { get; init; }
}
