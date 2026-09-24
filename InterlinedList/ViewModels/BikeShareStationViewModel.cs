using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>One docked-station row of the Bike share widget, pre-formatted.</summary>
public sealed class BikeShareStationViewModel
{
    public BikeShareStationViewModel(BikeShareStation station)
    {
        Name = string.IsNullOrWhiteSpace(station.Name) ? "Station" : station.Name!;
        Detail = $"{station.Bikes ?? 0} bikes · {station.Docks ?? 0} docks";
        Distance = station.DistanceKm is { } km
            ? BikeShareWidgetViewModel.FormatDistance(km)
            : "";
    }

    public string Name { get; }
    public string Detail { get; }
    public string Distance { get; }
}
