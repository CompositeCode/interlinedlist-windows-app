using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The <c>kind: "list"</c> artifact on its way <b>out</b> — what a user's edits
/// to a Powered Template preview get serialized into for
/// <c>POST /api/ai/generate</c>.
///
/// <b>Why this exists alongside <see cref="AiListArtifact"/>.</b> That type is
/// the read side: it deserializes a <c>/suggest</c> response, so its
/// <c>Rows</c> are <c>Dictionary&lt;string, JsonElement&gt;</c> — every cell
/// already a parsed JSON value. Cells a user has just typed aren't: they're
/// strings, numbers, bools and nulls that the client decides on per column
/// type. Forcing those through <see cref="JsonElement"/> would mean serializing
/// each cell to a document and re-parsing it one value at a time, purely to
/// satisfy the read type. So the write side takes <c>object?</c> cells and lets
/// <see cref="System.Text.Json"/> do it once, for the whole artifact.
///
/// The wire shape is identical to what <c>/suggest</c> returned — verified live
/// 2026-09-16:
/// <code>
/// { "kind":"list", "title":"Reading List", "description":"Track books…",
///   "dsl":{ "name":…, "description":…, "fields":[…] },
///   "rows":[ {"title":"The Hobbit","author":"J.R.R. Tolkien","status":"Finished","rating":5},
///            {"title":"Project Hail Mary","author":"Andy Weir","status":"Reading","rating":null} ] }
/// </code>
/// Note rows are flat maps keyed by field key, and <c>null</c> is a legitimate
/// cell value — so unset cells are sent as null rather than omitted.
/// </summary>
public sealed class AiListArtifactPayload : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.List;

    public string Title { get; init; } = "";

    public string? Description { get; init; }

    /// <summary>
    /// The List Schema DSL object, rebuilt by
    /// <see cref="AiListDsl.ToElement"/>. Stays a <see cref="JsonElement"/>
    /// because the DSL is only partially reverse engineered and every member
    /// this client doesn't understand has to survive the round trip untouched.
    /// </summary>
    public JsonElement? Dsl { get; init; }

    /// <summary>Starter rows: one flat map per row, keyed by field key.</summary>
    public List<Dictionary<string, object?>> Rows { get; init; } = new();

    public object ToWire()
    {
        var wire = new Dictionary<string, object?>
        {
            ["kind"] = Kind.ToWire(),
            ["title"] = Title
        };

        if (Description is { Length: > 0 }) wire["description"] = Description;
        if (Dsl is { } dsl && dsl.ValueKind != JsonValueKind.Undefined) wire["dsl"] = dsl;
        if (Rows.Count > 0) wire["rows"] = Rows;

        return wire;
    }
}
