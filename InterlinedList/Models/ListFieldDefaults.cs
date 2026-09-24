using System.Globalization;
using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The defaultValue encoding rules for a column, which differ between the two
/// schema write shapes and are NOT symmetric with how a default reads back.
/// All three behaviours were live-probed 2026-09-16 against throwaway lists
/// (since deleted) and are load-bearing — a naive "just send the value" here
/// either loses the default or 500s the whole request:
///
/// <list type="bullet">
///   <item><b>DSL</b> (<c>POST /api/lists</c>, the destructive rebuild) takes the
///   RAW typed value: <c>3</c>, <c>false</c>, <c>"open"</c>. The server stores it
///   JSON-encoded (<c>"3"</c>, <c>"false"</c>, <c>"\"open\""</c>).</item>
///   <item><b>properties</b> (the non-destructive PUT) takes only a JSON STRING.
///   A raw number or boolean answers <c>500 Internal server error</c> and the
///   WHOLE request is discarded — so a default is written as the unquoted
///   literal text (<c>"7"</c>, <c>"true"</c>, <c>"hello"</c>).</item>
///   <item><b>Reads</b> come back two ways: <c>GET /api/lists/{id}</c> →
///   <c>properties[].defaultValue</c> is the stored, still-encoded string, while
///   <c>GET /api/lists/{id}/schema</c> → <c>fields[].defaultValue</c> is
///   JSON.parse'd (<c>"3"</c>→<c>3</c>, <c>"\"open\""</c>→<c>"open"</c>), falling
///   back to the raw string when it isn't valid JSON.</item>
/// </list>
///
/// One documented quirk of the properties path: a <i>text</i> default that reads
/// like JSON (<c>7</c>, <c>true</c>) decodes to that scalar in the schema view.
/// It round-trips to the same editor text, so the editor is consistent either way.
/// </summary>
public static class ListFieldDefaults
{
    /// <summary>
    /// Stored (still-encoded) <c>properties[].defaultValue</c> → text for an editor
    /// box. Unwraps the one level of JSON encoding the server applies, so
    /// <c>"\"open\""</c> shows as <c>open</c> and <c>"3"</c> as <c>3</c>.
    /// </summary>
    public static string FromStored(JsonElement? stored)
    {
        if (stored is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;

        if (element.ValueKind != JsonValueKind.String)
            return element.GetRawText();

        var text = element.GetString() ?? string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? string.Empty
                : text;
        }
        catch (JsonException)
        {
            // Not JSON — it's already the plain value (how the properties path stores it).
            return text;
        }
    }

    /// <summary>
    /// Decoded <c>fields[].defaultValue</c> from <c>GET …/schema</c> → editor text.
    /// The value arrives as a <see cref="JsonElement"/> because
    /// <see cref="ListField.DefaultValue"/> is typed <c>object?</c>.
    /// </summary>
    public static string FromDecoded(object? decoded) => decoded switch
    {
        null => string.Empty,
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => e.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => e.GetRawText()
        },
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString() ?? string.Empty
    };

    /// <summary>
    /// Editor text → the <c>properties</c> body's <c>defaultValue</c>: always a
    /// JSON string, never a raw number/boolean (those 500 the request), or null
    /// to clear it. Note that OMITTING it clears the stored default, so the
    /// caller must send this on every item it means to keep.
    /// </summary>
    public static JsonElement? ToPropertyDefault(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : JsonSerializer.SerializeToElement(text.Trim());

    /// <summary>
    /// Editor text → the DSL's <c>defaultValue</c>: a raw typed CLR value, so a
    /// number column pre-fills with a number rather than the string "3".
    /// Returns null for blank text or text that doesn't parse for the type —
    /// callers validate first (see the column editor) so unparseable text is
    /// reported rather than silently dropped.
    /// </summary>
    public static object? ToDslDefault(string? type, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        return type switch
        {
            ListFieldType.Number => TryParseNumber(trimmed, out var number) ? number : null,
            ListFieldType.Boolean => TryParseBoolean(trimmed, out var flag) ? flag : null,
            _ => trimmed
        };
    }

    public static bool TryParseNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Accepts the wire spelling (<c>true</c>/<c>false</c>) case-insensitively.</summary>
    public static bool TryParseBoolean(string? text, out bool value)
    {
        value = false;
        if (text is null) return false;
        var trimmed = text.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Hint text for the default box, so the per-type encoding isn't a guessing game.</summary>
    public static string EditorHint(string? type) => type switch
    {
        ListFieldType.Number => "e.g. 0",
        ListFieldType.Boolean => "true or false",
        ListFieldType.Date => "YYYY-MM-DD",
        ListFieldType.DateTime => "YYYY-MM-DDTHH:MM:SSZ",
        ListFieldType.MultiSelect => "comma-separated options",
        _ => "optional"
    };
}
