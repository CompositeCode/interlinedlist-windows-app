using System.Text;
using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// JSON helpers shared by the AI models. Same web (camelCase) defaults the rest
/// of <see cref="InterlinedList.Services.InterlinedApiClient"/> uses; kept here
/// so the model types can project a raw artifact element into a typed payload
/// without reaching into the client's private options.
/// </summary>
internal static class AiJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serialize an anonymous/dictionary body into a detached JsonElement.</summary>
    internal static JsonElement ToElement(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, Options));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Parse a response body, tolerating raw (unescaped) control characters
    /// inside string literals. AI output is long-form markdown and this API has
    /// been observed emitting bodies that a strict RFC-8259 parser rejects —
    /// Python needs json.loads(..., strict=False) for them, and System.Text.Json
    /// is strict with no equivalent switch. So: try strict first, and only if
    /// that fails re-escape control characters that occur inside strings and
    /// retry. Returns a detached (cloned) element.
    /// </summary>
    internal static JsonElement ParseLenient(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(EscapeControlCharsInStrings(json));
            return doc.RootElement.Clone();
        }
    }

    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;

        foreach (var c in json)
        {
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (inString && c == '\\')
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString && c < ' ')
            {
                sb.Append(c switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => $"\\u{(int)c:x4}"
                });
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static string? StringOrNull(this JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool BoolOrFalse(this JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.True;

    internal static JsonElement? ObjectOrNull(this JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
}
