using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// A readable, editable view over the <c>dsl</c> object inside a
/// <c>powered_template</c> list artifact — enough to render the proposed
/// <b>columns</b> (type, label, required) in a table and let a user fix them
/// before confirming, which is what #14 asks for and what a raw JSON blob
/// can't give you.
///
/// <b>Live-verified shape</b> (POST /api/ai/suggest, feature
/// <c>powered_template</c>, input "a reading list with title, author, status,
/// and rating", 2026-09-16 — one quota unit):
/// <code>
/// "dsl": {
///   "name": "Reading List",
///   "description": "A personal list to track books, …",
///   "fields": [
///     { "key":"title",  "type":"text",   "label":"Title",        "required":true,  "displayOrder":0 },
///     { "key":"author", "type":"text",   "label":"Author",       "required":true,  "displayOrder":1 },
///     { "key":"status", "type":"select", "label":"Status",        "required":true,  "displayOrder":2,
///       "options":["To Read","Reading","Finished","Abandoned"], "defaultValue":"To Read" },
///     { "key":"rating", "type":"number", "label":"Rating (1-5)", "required":false, "displayOrder":3,
///       "visibility":{ "condition":{ "field":"status","operator":"equals","value":"Finished" } } }
///   ]
/// }
/// </code>
/// Before this, the list artifact was contract-transcribed only (#137's doc
/// comment says as much) — the <c>dsl</c> wrapper's <c>name</c>/<c>description</c>
/// members and the per-field <c>displayOrder</c>, <c>options</c>,
/// <c>defaultValue</c> and <c>visibility.condition</c> members were not on
/// record anywhere in this repo until now.
///
/// <b>Why every field keeps its raw element.</b> CLAUDE.md records the List
/// Schema DSL as only partially reverse engineered, and the probe above proves
/// it: <c>visibility.condition</c> and <c>defaultValue</c> showed up unannounced
/// on the very first call. So this type is deliberately a <i>partial</i>
/// projection — it parses the five members the column editor needs and
/// round-trips everything else byte-for-byte out of <see cref="Raw"/>. Editing a
/// label must not silently drop a conditional-visibility rule the model wrote,
/// and /generate re-validates the DSL server-side, so sending back exactly what
/// came out (minus the deliberate edits) is the only safe default.
/// </summary>
public sealed class AiListDsl
{
    /// <summary>The DSL's own name. Distinct from the artifact's <c>title</c>, which is what the list is called.</summary>
    public string? Name { get; init; }

    public string? Description { get; init; }

    public List<AiListDslField> Fields { get; init; } = new();

    /// <summary>The whole <c>dsl</c> object as it arrived, so unknown top-level members survive an edit.</summary>
    public JsonElement Raw { get; init; }

    /// <summary>Documented ceiling: a DSL carries at most 20 fields.</summary>
    public const int MaxFields = 20;

    /// <summary>
    /// Documented key rule: <c>^[a-z][a-z0-9_-]*$</c>. Mirrored so a
    /// user-added column can be given a legal key locally instead of learning
    /// it was illegal from a failed /generate — which would cost a quota unit.
    /// </summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 }
        && key[0] is >= 'a' and <= 'z'
        && key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

    /// <summary>Slugify a user-typed label into a legal key, or null if nothing usable survives.</summary>
    public static string? KeyFromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var chars = label.Trim().ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_')
            .ToArray();

        var slug = new string(chars).Trim('_');
        while (slug.Contains("__")) slug = slug.Replace("__", "_");

        // A key must start with a letter; prefix rather than drop a leading digit.
        if (slug.Length > 0 && slug[0] is >= '0' and <= '9') slug = "f_" + slug;

        return IsValidKey(slug) ? slug : null;
    }

    public static AiListDsl? FromJson(JsonElement? element)
    {
        if (element is not { } dsl || dsl.ValueKind != JsonValueKind.Object) return null;

        var fields = new List<AiListDslField>();
        if (dsl.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fieldsElement.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object) continue;
                fields.Add(AiListDslField.FromJson(field));
            }
        }

        return new AiListDsl
        {
            Name = dsl.StringOrNull("name"),
            Description = dsl.StringOrNull("description"),
            Fields = fields.OrderBy(f => f.DisplayOrder).ToList(),
            Raw = dsl.Clone()
        };
    }

    /// <summary>
    /// Rebuild the <c>dsl</c> object from an edited field set, preserving every
    /// top-level member that isn't <c>fields</c> (or the two this type owns).
    /// <paramref name="fields"/> is re-numbered into <c>displayOrder</c> so a
    /// user who deleted a column doesn't leave a gap in the sequence.
    /// </summary>
    public JsonElement ToElement(string? name, string? description, IReadOnlyList<AiListDslField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var wire = new Dictionary<string, object?>();

        if (Raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in Raw.EnumerateObject())
            {
                if (property.NameEquals("fields") || property.NameEquals("name") || property.NameEquals("description"))
                    continue;
                wire[property.Name] = property.Value;
            }
        }

        if (name is { Length: > 0 }) wire["name"] = name;
        if (description is { Length: > 0 }) wire["description"] = description;

        wire["fields"] = fields
            .Select((field, index) => field.ToWire(index))
            .ToList();

        return AiJson.ToElement(wire);
    }

    /// <summary>An empty DSL, for building one from scratch if a suggestion came back without one.</summary>
    public static AiListDsl Empty() => new() { Raw = AiJson.ToElement(new Dictionary<string, object?>()) };
}

/// <summary>
/// One proposed column. The four members #14's preview has to show and let a
/// user change — <see cref="Key"/>, <see cref="Type"/>, <see cref="Label"/>,
/// <see cref="Required"/> — are parsed; everything else (<c>options</c>,
/// <c>defaultValue</c>, <c>visibility</c>, and whatever the DSL grows next)
/// rides along untouched in <see cref="Raw"/>.
/// </summary>
public sealed class AiListDslField
{
    /// <summary>Stable identifier, also the key each starter row uses. <c>^[a-z][a-z0-9_-]*$</c>.</summary>
    public string Key { get; init; } = "";

    /// <summary>Wire type. Observed live: <c>text</c>, <c>number</c>, <c>select</c>.</summary>
    public string Type { get; init; } = DefaultType;

    /// <summary>Human label shown as the column header.</summary>
    public string Label { get; init; } = "";

    public bool Required { get; init; }

    public int DisplayOrder { get; init; }

    /// <summary>The field object exactly as it arrived (empty for a user-added column).</summary>
    public JsonElement Raw { get; init; }

    public const string DefaultType = "text";

    /// <summary>
    /// The field types this client has actually seen the server emit. It is
    /// explicitly <b>not</b> the complete DSL type set — the DSL is only
    /// partially reverse engineered, so a column editor offers these plus
    /// whatever types the artifact in hand already uses, and invents nothing.
    /// </summary>
    public static IReadOnlyList<string> ObservedTypes { get; } = new[] { "text", "number", "select" };

    /// <summary>Options for a <c>select</c> column, read out of <see cref="Raw"/> for display.</summary>
    public IReadOnlyList<string> Options
    {
        get
        {
            if (Raw.ValueKind != JsonValueKind.Object
                || !Raw.TryGetProperty("options", out var options)
                || options.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return options.EnumerateArray()
                .Select(o => o.ValueKind == JsonValueKind.String ? o.GetString() : o.ToString())
                .Where(o => !string.IsNullOrEmpty(o))
                .Select(o => o!)
                .ToList();
        }
    }

    /// <summary>
    /// True when this field carries DSL members the column editor doesn't
    /// surface (options, defaultValue, a visibility condition, …). The preview
    /// says so, so a user isn't surprised that a column does more than its
    /// three editable attributes suggest.
    /// </summary>
    public bool HasExtraRules =>
        Raw.ValueKind == JsonValueKind.Object
        && Raw.EnumerateObject().Any(p =>
            !p.NameEquals("key") && !p.NameEquals("type") && !p.NameEquals("label")
            && !p.NameEquals("required") && !p.NameEquals("displayOrder"));

    public static AiListDslField FromJson(JsonElement field) => new()
    {
        Key = field.StringOrNull("key") ?? "",
        Type = field.StringOrNull("type") ?? DefaultType,
        Label = field.StringOrNull("label") ?? field.StringOrNull("key") ?? "",
        Required = field.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True,
        DisplayOrder = field.TryGetProperty("displayOrder", out var order)
                       && order.ValueKind == JsonValueKind.Number
                       && order.TryGetInt32(out var value)
            ? value
            : int.MaxValue,
        Raw = field.Clone()
    };

    /// <summary>
    /// Serialize back, overwriting only the five members this type owns and
    /// copying every other member across verbatim.
    /// </summary>
    public Dictionary<string, object?> ToWire(int displayOrder)
    {
        var wire = new Dictionary<string, object?>();

        if (Raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in Raw.EnumerateObject())
            {
                if (property.NameEquals("key") || property.NameEquals("type") || property.NameEquals("label")
                    || property.NameEquals("required") || property.NameEquals("displayOrder"))
                    continue;
                wire[property.Name] = property.Value;
            }
        }

        wire["key"] = Key;
        wire["type"] = Type;
        wire["label"] = Label;
        wire["required"] = Required;
        wire["displayOrder"] = displayOrder;

        return wire;
    }
}
