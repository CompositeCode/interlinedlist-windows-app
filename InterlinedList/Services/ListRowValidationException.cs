using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// A refused row write, carrying the per-field <c>details</c> the shared
/// <c>EnsureSuccessAsync</c> throws away (it keeps only status + the top-level
/// <c>error</c> string, which for these is always the useless "Validation
/// failed"). The typed row editor needs the details to put a message on the
/// control that caused it.
///
/// Live-verified 2026-09-16 on a throwaway list: <c>POST</c> and <c>PUT</c>
/// <c>/api/lists/{id}/data</c> both answer <c>422</c> with
/// <c>details:[{field,message}]</c> — <c>field</c> is the column key, the
/// message is phrased with its label — for a missing required value, a
/// malformed email/url/date, a number outside <c>validation.min</c>/<c>max</c>,
/// a non-boolean in a boolean column, a select value outside its options, and a
/// multiselect that isn't an array. Several details can arrive at once.
///
/// Not an <see cref="InterlinedApiException"/> because that type is sealed; only
/// the two row-write methods in <c>InterlinedApiClient.Lists.cs</c> throw this,
/// and their XML docs say so.
/// </summary>
public sealed class ListRowValidationException : Exception
{
    public int StatusCode { get; }

    /// <summary>The server's <c>code</c> ("validation_failed"), when present.</summary>
    public string? Code { get; }

    /// <summary>One entry per offending column; empty when the body named none.</summary>
    public IReadOnlyList<ListRowFieldError> Details { get; }

    public ListRowValidationException(
        int statusCode, string message, string? code = null,
        IReadOnlyList<ListRowFieldError>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details ?? [];
    }

    /// <summary>
    /// Build from a non-success row-write response. Mirrors
    /// <c>EnsureSuccessAsync</c>'s "prefer the JSON error property" behaviour and
    /// then adds the details array.
    /// </summary>
    public static async Task<ListRowValidationException> FromResponseAsync(
        HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        var message = body;
        string? code = null;
        var details = new List<ListRowFieldError>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } text)
                message = text;
            if (doc.RootElement.TryGetProperty("code", out var codeProp))
                code = codeProp.GetString();
            if (doc.RootElement.TryGetProperty("details", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var field = item.TryGetProperty("field", out var f) ? f.GetString() : null;
                    var detail = item.TryGetProperty("message", out var m) ? m.GetString() : null;
                    if (field is { Length: > 0 } && detail is { Length: > 0 })
                        details.Add(new ListRowFieldError(field, detail));
                }
            }
        }
        catch (JsonException)
        {
            // Body wasn't JSON — surface the raw text, same as EnsureSuccessAsync.
        }

        // With details present the top-level "Validation failed" says nothing the
        // per-field messages don't say better, so lead with those.
        if (details.Count > 0)
            message = string.Join(" ", details.Select(d => d.Message));

        return new ListRowValidationException((int)resp.StatusCode, message, code, details);
    }
}
