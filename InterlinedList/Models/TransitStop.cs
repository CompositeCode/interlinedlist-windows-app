using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One nearby stop from GET /api/widgets/transit/stops.
///
/// <para><b>This element shape is NOT live-verified.</b> Every accepted
/// <c>agency</c> value returned <c>"stops": []</c> along
/// <c>"error": "unavailable"</c> on 2026-09-16 (the server's upstream transit
/// feed is down), and the OpenAPI spec types the whole response as a bare
/// <c>{"type":"object"}</c> with no element schema. So rather than invent a
/// strict contract, every property here is nullable and
/// <see cref="Extra"/> catches anything this class does not name — nothing is
/// silently dropped, and the UI reads <see cref="DisplayName"/>, which falls
/// back gracefully when none of the guessed names are present.</para>
///
/// <para>Per this repo's convention, do not tighten these types until a real
/// populated response has been captured from a test account.</para>
/// </summary>
public sealed class TransitStop
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Direction { get; init; }
    public double? DistanceKm { get; init; }
    public List<string>? Routes { get; init; }

    /// <summary>Anything the live payload carries that this class does not name.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Best available human label. Falls through the named guesses, then any
    /// string-valued extension field, then a generic word — so an unexpected
    /// payload still renders something instead of a blank row.
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name!;
            if (!string.IsNullOrWhiteSpace(Id)) return Id!;

            if (Extra is not null)
                foreach (var (_, value) in Extra)
                    if (value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { Length: > 0 } text)
                        return text;

            return "Stop";
        }
    }
}
