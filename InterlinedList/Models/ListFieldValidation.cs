using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// A column's extra validation rules. These are enforced on ROW writes
/// (POST/PUT /api/lists/{id}/data), never when the schema itself is saved —
/// verified live 2026-09-16: a row breaching validation.max answers
/// <c>422 { "error": "Validation failed", "code": "validation_failed",
/// "details": [ { "field": "qty", "message": "Quantity must be at most 100" } ] }</c>.
///
/// Every property is written only when non-null: an all-null validation object
/// is accepted but is stored verbatim as
/// <c>validationRules: { min: null, max: null, … }</c> junk (observed live).
/// </summary>
public sealed class ListFieldValidation
{
    /// <summary>number: a number; date/datetime: an ISO date string. Hence object.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Min { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Max { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; init; }

    /// <summary>Regex for text/textarea/email. Server message on failure: "&lt;label&gt; format is invalid".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pattern { get; init; }

    /// <summary>Number-input increment. A UI hint only — never enforced on the stored value.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Step { get; init; }

    [JsonIgnore]
    public bool IsEmpty =>
        Min is null && Max is null && MinLength is null && MaxLength is null
        && Pattern is null && Step is null;
}
