using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// GET /api/widgets/bike-share?lat=&amp;lon=&amp;radius=&amp;system= — nearby
/// bike/scooter availability. Parameter names are <c>lat</c>/<c>lon</c> (NOT
/// latitude/longitude, which is what /api/weather and /api/location use —
/// the two families genuinely differ), confirmed against the live OpenAPI spec
/// and by probe.
///
/// <para><b>Two payload shapes behind one path, discriminated by
/// <see cref="Kind"/>.</b> Live-probed 2026-09-16 across Seattle, NYC, Chicago,
/// SF and lat/lon 0,0; the properties below are the *union* of keys across all
/// of those responses, so any single response leaves several null:</para>
/// <list type="bullet">
/// <item><description><c>kind: "dockless"</c> (Seattle → "Lime (Seattle)")
/// populates <see cref="Summary"/> and <see cref="Points"/>; the points carry
/// only kind/lat/lon. <see cref="Stations"/> and <see cref="Within"/> are
/// absent.</description></item>
/// <item><description><c>kind: "docked"</c> (NYC → "Citi Bike (NYC)", Chicago →
/// "Divvy (Chicago)", SF → "Bay Wheels (SF)") populates
/// <see cref="Stations"/> (the 3 nearest) and <see cref="Within"/> totals; its
/// points additionally carry name/bikes/docks. <see cref="Summary"/> is
/// absent.</description></item>
/// </list>
///
/// <para>With no coordinates at all the call still returns
/// <c>200 {"system":null,"systemUrl":null,"kind":"docked","radiusMeters":402,
/// "stations":[],"within":{...0},"points":[]}</c> — it does not 400. A null
/// <see cref="SystemName"/> therefore means "no system covers this point"
/// (lat/lon 0,0 behaves identically), which the widget reports as a plain
/// "nothing nearby" state rather than an error.</para>
///
/// <para><c>radius</c> and <c>system</c> are accepted but had no observable
/// effect: within a single probe batch, radius 2000 and radius 8000 and
/// system=lime and system=&lt;nonsense&gt; all returned byte-identical payloads
/// that still echoed <c>radiusMeters: 402</c> (~0.25 mile), so the server
/// appears to clamp both to its own defaults. The client therefore does not
/// bother sending them.</para>
/// </summary>
public sealed class BikeShareWidget
{
    // "System" would shadow the System namespace inside this file, so the CLR
    // name differs from the wire name here on purpose.
    [JsonPropertyName("system")]
    public string? SystemName { get; init; }

    public string? SystemUrl { get; init; }

    /// <summary>"docked" or "dockless" — selects which of the members below are populated.</summary>
    public string? Kind { get; init; }

    public int? RadiusMeters { get; init; }

    /// <summary>Docked systems only: the nearest few stations.</summary>
    public List<BikeShareStation> Stations { get; init; } = new();

    /// <summary>Docked systems only: totals inside the search radius.</summary>
    public BikeShareWithin? Within { get; init; }

    /// <summary>Dockless systems only: vehicle counts inside the search radius.</summary>
    public BikeShareSummary? Summary { get; init; }

    /// <summary>Map pins — every vehicle (dockless) or station (docked) in range.</summary>
    public List<BikeSharePoint> Points { get; init; } = new();

    [JsonIgnore]
    public bool IsDockless =>
        string.Equals(Kind, "dockless", StringComparison.OrdinalIgnoreCase);
}
