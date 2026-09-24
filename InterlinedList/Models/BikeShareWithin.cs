namespace InterlinedList.Models;

/// <summary>
/// Docked-system totals inside the search radius (the <c>within</c> object).
/// Note <c>stations</c> here is a <i>count</i>, unlike the sibling
/// <c>stations</c> array on <see cref="BikeShareWidget"/>.
/// </summary>
public sealed class BikeShareWithin
{
    public int? Bikes { get; init; }
    public int? Docks { get; init; }
    public int? Stations { get; init; }
}
