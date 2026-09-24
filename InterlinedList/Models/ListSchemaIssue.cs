using System.Text.RegularExpressions;

namespace InterlinedList.Models;

/// <summary>
/// One thing wrong with a schema, attributable to a single column where the
/// server names one. Produced two ways, deliberately in the same shape so the
/// UI can attach either to the offending field row:
/// <list type="bullet">
///   <item><see cref="ListSchema.Validate"/> — client-side, before the request.</item>
///   <item><see cref="FromServerMessage"/> — parsed out of a real 400 body.</item>
/// </list>
/// The server's messages (all captured live 2026-09-16) are:
/// <code>
/// Invalid schema: DSL must have a 'name' property (string)
/// Invalid schema: DSL must have at least one field
/// Invalid schema: DSL must be an object
/// Invalid schema: Field at index 0 must have a 'key' property (string)
/// Invalid schema: Field 'a' must have a 'label' property (string)
/// Invalid schema: Field 'year' has invalid type 'integer'. Valid types: text, number, date, …
/// Invalid schema: Field 's' (type: select) must have an 'options' array
/// Invalid schema: Duplicate field keys found: a, b
/// // …and from the non-destructive properties form:
/// Unknown propertyType 'textarea'. Allowed: text, number, boolean, date, url, email
/// Duplicate propertyKey 'title' in request
/// Property id '…' does not exist on this list
/// propertyKey cannot change for an existing property; rename propertyName instead
/// </code>
/// </summary>
public sealed record ListSchemaIssue(string Message, string? FieldKey = null, int? FieldIndex = null)
{
    private const string Prefix = "Invalid schema: ";

    /// <summary>Message with the server's "Invalid schema: " prefix stripped, for inline display.</summary>
    public string ShortMessage =>
        Message.StartsWith(Prefix, StringComparison.Ordinal) ? Message[Prefix.Length..] : Message;

    /// <summary>
    /// Split a 400 error string into per-column issues. Always returns at least
    /// one entry (the raw message, unattributed, when nothing matches) so a
    /// caller never has to special-case an empty result.
    /// </summary>
    public static List<ListSchemaIssue> FromServerMessage(string? message)
    {
        var text = message ?? string.Empty;

        // "Duplicate field keys found: a, b" / "Duplicate propertyKey 'title' in request"
        var dup = Regex.Match(text, @"Duplicate field keys found:\s*(.+)$");
        if (dup.Success)
        {
            var keys = dup.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => k.Trim('\'', '"'))
                .Where(k => k.Length > 0)
                .ToList();
            if (keys.Count > 0)
                return keys.Select(k => new ListSchemaIssue(text, k)).ToList();
        }

        // Any message that quotes a single field key: Field 'x' …, propertyKey 'x' …
        // ("Unknown propertyType 'textarea'" is deliberately NOT matched — the
        // quoted token there is a type, not a column key.)
        var keyed = Regex.Match(text, @"(?:Field|propertyKey)\s+'([^']+)'");
        if (keyed.Success)
            return [new ListSchemaIssue(text, keyed.Groups[1].Value)];

        // "Field at index 0 must have a 'key' property (string)"
        var indexed = Regex.Match(text, @"Field at index (\d+)");
        if (indexed.Success && int.TryParse(indexed.Groups[1].Value, out var index))
            return [new ListSchemaIssue(text, null, index)];

        return [new ListSchemaIssue(text)];
    }
}
