using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// A failed schema write, carrying the two things the schema UI needs and that
/// a plain message string loses: the per-column
/// <see cref="ListSchemaIssue"/>s parsed out of a 400 body, and the
/// <c>propertiesWithData</c> array a 409 returns when a non-destructive edit
/// would delete a column that still holds row data.
///
/// It is NOT an <see cref="InterlinedApiException"/> because that type is sealed
/// and carries only status + message; only the four schema methods in
/// <c>InterlinedApiClient.Lists.cs</c> throw this, and their XML docs say so.
/// </summary>
public sealed class ListSchemaException : Exception
{
    public int StatusCode { get; }

    /// <summary>The server's <c>code</c> field ("bad_request", "conflict", …), when present.</summary>
    public string? Code { get; }

    /// <summary>One entry per offending column where the server named one; never empty.</summary>
    public IReadOnlyList<ListSchemaIssue> Issues { get; }

    /// <summary>
    /// Column keys that still hold row data and therefore block their deletion.
    /// Re-issue the same edit with <c>force: true</c> to drop them (which strips
    /// the key from every row).
    /// </summary>
    public IReadOnlyList<string> PropertiesWithData { get; }

    /// <summary>409 + at least one blocked column — the "confirm data loss" case.</summary>
    public bool RequiresForce => StatusCode == 409 && PropertiesWithData.Count > 0;

    public ListSchemaException(
        int statusCode,
        string message,
        string? code = null,
        IReadOnlyList<ListSchemaIssue>? issues = null,
        IReadOnlyList<string>? propertiesWithData = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Issues = issues is { Count: > 0 } ? issues : ListSchemaIssue.FromServerMessage(message);
        PropertiesWithData = propertiesWithData ?? [];
    }

    /// <summary>Client-side failure (from <see cref="ListSchema.Validate"/>) reported in the server's shape.</summary>
    public static ListSchemaException FromIssues(IReadOnlyList<ListSchemaIssue> issues) =>
        new(400,
            issues.Count > 0 ? string.Join(" ", issues.Select(i => i.Message)) : "Invalid schema.",
            "bad_request",
            issues);

    /// <summary>
    /// Build from a non-success response. Mirrors the shared
    /// EnsureSuccessAsync's "prefer the JSON error property" behaviour and then
    /// adds the schema-specific fields.
    /// </summary>
    public static async Task<ListSchemaException> FromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        var message = body;
        string? code = null;
        List<string>? withData = null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } text)
                message = text;
            if (doc.RootElement.TryGetProperty("code", out var codeProp))
                code = codeProp.GetString();
            if (doc.RootElement.TryGetProperty("propertiesWithData", out var arr) && arr.ValueKind == JsonValueKind.Array)
                withData = arr.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList();
        }
        catch (JsonException)
        {
            // Body wasn't JSON — surface the raw text, same as EnsureSuccessAsync.
        }

        return new ListSchemaException((int)resp.StatusCode, message, code, null, withData);
    }
}
