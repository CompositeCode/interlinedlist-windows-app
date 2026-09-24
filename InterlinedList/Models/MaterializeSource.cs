using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// The source half of a POST /api/materialize request: an <b>id-only</b>
/// reference, discriminated on <see cref="Kind"/>.
/// <para>
/// This type deliberately has no way to carry content. The server re-fetches
/// and re-authorizes every referenced id under the calling user and derives the
/// new list/document/draft from <em>that</em> authoritative data — client-supplied
/// cell values or body text are never trusted. Accepting content here would only
/// invite callers to believe it mattered. The one exception is
/// <see cref="Markdown"/> on <see cref="MaterializeSourceKind.DocElements"/>,
/// where the markdown is not payload but the <em>selector</em> identifying which
/// part of an already-authorized document was selected.
/// </para>
/// <para>
/// Referencing anything you do not own returns <c>404</c>
/// ("One or more messages are unavailable" / "Document is unavailable" /
/// "One or more lists are unavailable" / "One or more rows are unavailable" —
/// all verified live 2026-09-16). Source resolution is the endpoint's <em>first</em>
/// gate: a bad id fails before anything is created.
/// </para>
/// Request-only — never deserialized, hence the private constructor and the
/// factory methods.
/// </summary>
public sealed class MaterializeSource
{
    private MaterializeSource(MaterializeSourceKind kind) => Kind = kind;

    public MaterializeSourceKind Kind { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? MessageIds { get; private init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ListIds { get; private init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ListId { get; private init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RowIds { get; private init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentId { get; private init; }

    /// <summary>
    /// The selected markdown for <see cref="MaterializeSourceKind.DocElements"/>.
    /// Identifies <em>which part</em> of <see cref="DocumentId"/> was selected; the
    /// document itself is still re-fetched and authorized server-side.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Markdown { get; private init; }

    /// <summary>One or more messages. Several messages combine into one result.</summary>
    public static MaterializeSource FromMessages(IEnumerable<string> messageIds) =>
        new(MaterializeSourceKind.Messages) { MessageIds = Ids(messageIds, nameof(messageIds)) };

    /// <summary>One or more whole lists (schema + rows).</summary>
    public static MaterializeSource FromLists(IEnumerable<string> listIds) =>
        new(MaterializeSourceKind.Lists) { ListIds = Ids(listIds, nameof(listIds)) };

    /// <summary>Selected rows from a single list.</summary>
    public static MaterializeSource FromRows(string listId, IEnumerable<string> rowIds) =>
        new(MaterializeSourceKind.Rows)
        {
            ListId = Id(listId, nameof(listId)),
            RowIds = Ids(rowIds, nameof(rowIds))
        };

    /// <summary>A whole document.</summary>
    public static MaterializeSource FromDocument(string documentId) =>
        new(MaterializeSourceKind.Document) { DocumentId = Id(documentId, nameof(documentId)) };

    /// <summary>
    /// A selection inside a document. <paramref name="markdown"/> is the selected
    /// markdown — the selector for the selection, not trusted content.
    /// </summary>
    public static MaterializeSource FromDocumentElements(string documentId, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            throw new ArgumentException("A docElements source needs the selected markdown.", nameof(markdown));

        return new MaterializeSource(MaterializeSourceKind.DocElements)
        {
            DocumentId = Id(documentId, nameof(documentId)),
            Markdown = markdown
        };
    }

    private static string Id(string id, string paramName) =>
        string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Id must not be empty.", paramName)
            : id;

    private static IReadOnlyList<string> Ids(IEnumerable<string> ids, string paramName)
    {
        ArgumentNullException.ThrowIfNull(ids, paramName);
        var list = ids.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one id is required.", paramName);
        if (list.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Ids must not be empty.", paramName);
        return list;
    }
}
