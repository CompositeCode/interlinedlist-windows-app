using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The single typed envelope for "content an AI produced". POST /api/ai/suggest
/// returns one; POST /api/ai/generate takes the (optionally user-edited) same one
/// back and persists it. Every artifact has a <see cref="Kind"/>.
///
/// The raw JSON is kept alongside the parsed view on purpose: /generate
/// re-validates the artifact server-side, so round-tripping exactly what
/// /suggest sent is the safe default, and the two artifact shapes with
/// partially-reverse-engineered innards (a list's schema DSL, its starter rows)
/// stay as <see cref="JsonElement"/> instead of being typed on a guess.
///
/// Verified live 2026-09-16 (writing_assist /suggest):
///   {"kind":"message","content":"Shipped the new feature today — …"}
///   {"kind":"thread","parts":["We shipped the new thing today 🎉", …]}
/// The list / document / message_series / doc_series / tags shapes below come
/// from the published contract at /help/api/ai-integration and were NOT
/// exercised live (each costs quota, and only /suggest is non-persisting).
/// </summary>
public sealed class AiArtifact
{
    /// <summary>Raw wire discriminator. Unknown values are preserved rather than rejected.</summary>
    public required string Kind { get; init; }

    /// <summary>The artifact object exactly as it came off the wire (or as built from a payload).</summary>
    public required JsonElement Raw { get; init; }

    public AiArtifactKind? KnownKind => AiArtifactKinds.TryParse(Kind, out var kind) ? kind : null;

    /// <summary>
    /// False for the writing_assist kinds (message/thread/tags) and for any kind
    /// this client doesn't recognize. POST /api/ai/generate answers
    /// 422 invalid_input for those, so the client refuses them before spending a
    /// quota unit.
    /// </summary>
    public bool IsPersistable => KnownKind is { } kind && kind.IsPersistable();

    public static AiArtifact FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("An AI artifact must be a JSON object.", nameof(element));

        return new AiArtifact
        {
            Kind = element.StringOrNull("kind") ?? "",
            Raw = element.Clone()
        };
    }

    /// <summary>Build an artifact from a typed payload — the "user edited the preview, now persist it" path.</summary>
    public static AiArtifact FromPayload(IAiArtifactPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new AiArtifact
        {
            Kind = payload.Kind.ToWire(),
            Raw = AiJson.ToElement(payload.ToWire())
        };
    }

    // ── Typed projections (null when the artifact isn't that kind) ───────────────

    public AiListArtifact? AsList() => As<AiListArtifact>(AiArtifactKind.List);
    public AiDocumentArtifact? AsDocument() => As<AiDocumentArtifact>(AiArtifactKind.Document);
    public AiMessageSeriesArtifact? AsMessageSeries() => As<AiMessageSeriesArtifact>(AiArtifactKind.MessageSeries);
    public AiDocSeriesArtifact? AsDocSeries() => As<AiDocSeriesArtifact>(AiArtifactKind.DocSeries);
    public AiMessageArtifact? AsMessage() => As<AiMessageArtifact>(AiArtifactKind.Message);
    public AiThreadArtifact? AsThread() => As<AiThreadArtifact>(AiArtifactKind.Thread);
    public AiTagsArtifact? AsTags() => As<AiTagsArtifact>(AiArtifactKind.Tags);

    /// <summary>A one-line label for the preview header.</summary>
    public string Summary => KnownKind switch
    {
        AiArtifactKind.List => AsList()?.Title ?? "List",
        AiArtifactKind.Document => AsDocument()?.Title ?? "Document",
        AiArtifactKind.MessageSeries => AsMessageSeries() is { } s ? $"{s.ListTitle} · {s.Items.Count} messages" : "Message series",
        AiArtifactKind.DocSeries => AsDocSeries() is { } d ? $"{d.FolderTitle} · {d.Documents.Count} documents" : "Document series",
        AiArtifactKind.Message => AsMessage()?.Content ?? "Message",
        AiArtifactKind.Thread => AsThread() is { } t ? $"Thread · {t.Parts.Count} parts" : "Thread",
        AiArtifactKind.Tags => AsTags() is { } g ? string.Join(", ", g.Tags) : "Tags",
        _ => Kind
    };

    private T? As<T>(AiArtifactKind expected) where T : class
        => KnownKind == expected ? Raw.Deserialize<T>(AiJson.Options) : null;
}

/// <summary>
/// A typed artifact body. Implementations serialize themselves back into the
/// envelope shape /generate expects (kind + fields), omitting unset fields.
/// </summary>
public interface IAiArtifactPayload
{
    AiArtifactKind Kind { get; }

    /// <summary>The artifact object to send, including "kind".</summary>
    object ToWire();
}

/// <summary>
/// powered_template → a list. <see cref="Dsl"/> is a full List Schema DSL object
/// (max 20 fields, keys matching ^[a-z][a-z0-9_-]*$) and is left as raw JSON on
/// purpose: CLAUDE.md records the schema DSL as only partially reverse
/// engineered, so typing it here would be a guess that /generate re-validates.
/// Starter rows that fail schema validation are dropped server-side rather than
/// failing the whole artifact.
/// </summary>
public sealed class AiListArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.List;

    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public JsonElement? Dsl { get; init; }
    public List<Dictionary<string, JsonElement>> Rows { get; init; } = new();

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

/// <summary>powered_document → a single markdown document (capped at 40,000 characters server-side).</summary>
public sealed class AiDocumentArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.Document;

    public string Title { get; init; } = "";
    public string Markdown { get; init; } = "";
    public List<string> Outline { get; init; } = new();
    public bool IsPublic { get; init; }

    public object ToWire()
    {
        var wire = new Dictionary<string, object?>
        {
            ["kind"] = Kind.ToWire(),
            ["title"] = Title,
            ["markdown"] = Markdown,
            ["isPublic"] = IsPublic
        };
        if (Outline.Count > 0) wire["outline"] = Outline;
        return wire;
    }
}

/// <summary>
/// message_series → an ordered set of short messages (3–12 items, each capped at
/// 3,000 characters). On /generate the default is a list whose rows are scheduled
/// posts spaced a fixed 4 minutes apart; with scheduleImmediately it becomes
/// individual scheduled posts instead.
/// </summary>
public sealed class AiMessageSeriesArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.MessageSeries;

    public string ListTitle { get; init; } = "";
    public List<AiMessageSeriesItem> Items { get; init; } = new();

    public object ToWire() => new Dictionary<string, object?>
    {
        ["kind"] = Kind.ToWire(),
        ["listTitle"] = ListTitle,
        ["items"] = Items.Select(i => i.ToWire()).ToList()
    };
}

public sealed class AiMessageSeriesItem
{
    public int Order { get; init; }
    public string Content { get; init; } = "";

    /// <summary>
    /// The model's per-item channel hints. Used only for sizing — the real
    /// cross-post config comes from the /generate "crossPost" object, sanitized
    /// against the caller's own connected accounts.
    /// </summary>
    public List<string> CrossPostTargets { get; init; } = new();

    internal Dictionary<string, object?> ToWire()
    {
        var wire = new Dictionary<string, object?>
        {
            ["order"] = Order,
            ["content"] = Content
        };
        if (CrossPostTargets.Count > 0) wire["crossPostTargets"] = CrossPostTargets;
        return wire;
    }
}

/// <summary>article_series → a folder of documents (2–6 entries), created in order.</summary>
public sealed class AiDocSeriesArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.DocSeries;

    public string FolderTitle { get; init; } = "";
    public List<AiDocSeriesDocument> Documents { get; init; } = new();

    public object ToWire() => new Dictionary<string, object?>
    {
        ["kind"] = Kind.ToWire(),
        ["folderTitle"] = FolderTitle,
        ["documents"] = Documents.Select(d => d.ToWire()).ToList()
    };
}

public sealed class AiDocSeriesDocument
{
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Markdown { get; init; } = "";
    public List<string> Outline { get; init; } = new();

    internal Dictionary<string, object?> ToWire()
    {
        var wire = new Dictionary<string, object?>
        {
            ["order"] = Order,
            ["title"] = Title,
            ["markdown"] = Markdown
        };
        if (Outline.Count > 0) wire["outline"] = Outline;
        return wire;
    }
}

/// <summary>
/// writing_assist (rewrite/tighten/expand/grammar) → one rewritten draft.
/// NOT persistable — insert it into the composer client-side.
/// Shape verified live 2026-09-16.
/// </summary>
public sealed class AiMessageArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.Message;

    public string Content { get; init; } = "";

    public object ToWire() => new Dictionary<string, object?>
    {
        ["kind"] = Kind.ToWire(),
        ["content"] = Content
    };
}

/// <summary>
/// writing_assist (thread) → the draft split into thread parts.
/// NOT persistable. Shape verified live 2026-09-16.
/// </summary>
public sealed class AiThreadArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.Thread;

    public List<string> Parts { get; init; } = new();

    public object ToWire() => new Dictionary<string, object?>
    {
        ["kind"] = Kind.ToWire(),
        ["parts"] = Parts
    };
}

/// <summary>writing_assist (tags) → suggested tags. NOT persistable.</summary>
public sealed class AiTagsArtifact : IAiArtifactPayload
{
    public AiArtifactKind Kind => AiArtifactKind.Tags;

    public List<string> Tags { get; init; } = new();

    public object ToWire() => new Dictionary<string, object?>
    {
        ["kind"] = Kind.ToWire(),
        ["tags"] = Tags
    };
}
