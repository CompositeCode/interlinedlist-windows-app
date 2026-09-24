using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// A list's schema in DSL form — the shape GET /api/lists/{id}/schema returns
/// (under a <c>data</c> envelope) and the shape POST /api/lists and the
/// DESTRUCTIVE PUT /api/lists/{id}/schema accept as <c>schema</c>.
///
/// Live-verified 2026-09-16 against a throwaway list (since deleted):
/// <list type="bullet">
///   <item>GET …/schema → <c>200 { "data": { "name": …, "description"?: …, "fields": [ … ] } }</c>.</item>
///   <item><see cref="Name"/> and <see cref="Description"/> are the LIST's title
///   and description — the rebuild PUT writes them straight onto the list
///   (a null description clears it), and GET reads them back from it. There is
///   no separate stored schema name.</item>
///   <item>A list with no schema answers with <c>"fields": []</c>; sending that
///   back is rejected (<c>Invalid schema: DSL must have at least one field</c>),
///   which is why <see cref="Validate"/> catches it before the request.</item>
/// </list>
/// </summary>
public sealed class ListSchema
{
    /// <summary>
    /// Required, non-empty. On the rebuild PUT this BECOMES THE LIST'S TITLE
    /// (verified live: a list titled "ZZ Throwaway Schema Probe" came back
    /// titled "ZZ Renamed By DSL Rebuild" after the PUT).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional. On the rebuild PUT this overwrites the list's description —
    /// including clearing it when null, so echo the list's current description
    /// back unless the user is deliberately changing it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>At least one column; keys must be unique.</summary>
    public required List<ListField> Fields { get; init; }

    [JsonIgnore]
    public bool HasFields => Fields.Count > 0;

    /// <summary>
    /// True when every column's type is one of the six the non-destructive
    /// <c>properties</c> edit accepts. When false, a label rename or single
    /// column add can only be done by the destructive rebuild, which is exactly
    /// the fork the UI has to warn about.
    /// </summary>
    [JsonIgnore]
    public bool SupportsPropertiesEdit => HasFields && Fields.All(f => f.SupportsPropertiesEdit);

    [JsonIgnore]
    public IEnumerable<ListField> FieldsInDisplayOrder =>
        Fields.Select((f, i) => (Field: f, Order: f.DisplayOrder ?? i))
              .OrderBy(x => x.Order)
              .Select(x => x.Field);

    public ListField? FindField(string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Client-side mirror of the server's schema validator, so a bad schema is
    /// caught (and attached to the offending column) without a round-trip.
    /// Messages match the server's wording where the server has one. Also
    /// catches the two things the server does NOT check — an unknown visibility
    /// operator and a condition referencing a missing or later column — both of
    /// which the server silently accepts and then never satisfies.
    /// </summary>
    public List<ListSchemaIssue> Validate()
    {
        var issues = new List<ListSchemaIssue>();

        if (string.IsNullOrWhiteSpace(Name))
            issues.Add(new ListSchemaIssue("Invalid schema: DSL must have a 'name' property (string)"));

        if (!HasFields)
        {
            issues.Add(new ListSchemaIssue("Invalid schema: DSL must have at least one field"));
            return issues;
        }

        for (var i = 0; i < Fields.Count; i++)
        {
            var f = Fields[i];

            if (string.IsNullOrWhiteSpace(f.Key))
                issues.Add(new ListSchemaIssue(
                    $"Invalid schema: Field at index {i} must have a 'key' property (string)", null, i));

            if (string.IsNullOrWhiteSpace(f.Label))
                issues.Add(new ListSchemaIssue(
                    $"Invalid schema: Field '{f.Key}' must have a 'label' property (string)", f.Key, i));

            if (!ListFieldType.IsValid(f.Type))
                issues.Add(new ListSchemaIssue(
                    $"Invalid schema: Field '{f.Key}' has invalid type '{f.Type}'. " +
                    $"Valid types: {string.Join(", ", ListFieldType.All)}", f.Key, i));

            if (ListFieldType.RequiresOptions(f.Type) && f.Options is not { Count: > 0 })
                issues.Add(new ListSchemaIssue(
                    $"Invalid schema: Field '{f.Key}' (type: {f.Type}) must have an 'options' array",
                    f.Key, i));

            var condition = f.Visibility?.Condition;
            if (condition is not null)
            {
                if (!ListFieldOperator.IsValid(condition.Operator))
                    issues.Add(new ListSchemaIssue(
                        $"Field '{f.Key}' has an unknown visibility operator '{condition.Operator}'.",
                        f.Key, i));

                var watchedIndex = Fields.FindIndex(x => string.Equals(x.Key, condition.Field, StringComparison.Ordinal));
                if (watchedIndex < 0)
                    issues.Add(new ListSchemaIssue(
                        $"Field '{f.Key}' is shown conditionally on '{condition.Field}', which is not a column in this schema.",
                        f.Key, i));
                else if (watchedIndex >= i)
                    issues.Add(new ListSchemaIssue(
                        $"Field '{f.Key}' is shown conditionally on '{condition.Field}', which must appear earlier in the schema.",
                        f.Key, i));
            }
        }

        foreach (var group in Fields
                     .Where(f => !string.IsNullOrWhiteSpace(f.Key))
                     .GroupBy(f => f.Key, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            issues.Add(new ListSchemaIssue(
                $"Invalid schema: Duplicate field keys found: {group.Key}", group.Key));
        }

        return issues;
    }
}
