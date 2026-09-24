namespace InterlinedList.Models;

/// <summary>
/// The <c>coordinates</c> object on <see cref="LocationInfo"/> — the API's echo
/// of the requested point. Distinct from <see cref="GeoPoint"/>, which is this
/// app's own request-side value type.
/// </summary>
public sealed class GeoCoordinates
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
