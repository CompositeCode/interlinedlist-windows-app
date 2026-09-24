using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// Conditional visibility for one column: show it only while another column's
/// value satisfies <see cref="Condition"/>. Exactly one condition per column —
/// the DSL has no and/or grouping. Round-trip verified live 2026-09-16:
/// <c>"visibility": { "condition": { "field": "status", "operator": "equals", "value": "open" } }</c>
/// comes back unchanged from GET /api/lists/{id}/schema.
/// </summary>
public sealed class ListFieldVisibility
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ListFieldCondition? Condition { get; init; }
}

/// <summary>
/// One visibility test. <see cref="Field"/> is the <c>key</c> of the column to
/// watch and must appear EARLIER in <c>fields</c> (conditions are evaluated in
/// displayOrder). The server does not check that — a forward or unknown
/// reference saves happily and simply never matches — so
/// <see cref="ListSchema.Validate"/> checks it client-side instead.
/// </summary>
public sealed class ListFieldCondition
{
    public required string Field { get; init; }

    /// <summary>One of <see cref="ListFieldOperator"/>.</summary>
    public required string Operator { get; init; }

    /// <summary>Ignored by <c>isEmpty</c>/<c>isNotEmpty</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; init; }
}
