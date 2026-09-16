namespace InterlinedList.Models;

/// <summary>The four powered_document modes, set in context.mode.</summary>
public enum AiDocumentMode
{
    /// <summary>Default. No source — drafts from the input topic alone.</summary>
    Article,

    /// <summary>Requires context.listId. Server loads your list's schema + up to 50 rows.</summary>
    FromList,

    /// <summary>Requires context.documentId. Server loads your document's title + body.</summary>
    FromArticle,

    /// <summary>Requires context.url (http/https). Server fetches the page through an SSRF-guarded fetcher.</summary>
    ResearchUrl
}

/// <summary>context.action for writing_assist.</summary>
public enum AiWritingAssistAction
{
    Rewrite,
    Tighten,
    Expand,
    Grammar,
    Thread,
    Tags
}

public static class AiDocumentModes
{
    public static string ToWire(this AiDocumentMode mode) => mode switch
    {
        AiDocumentMode.Article => "article",
        AiDocumentMode.FromList => "from_list",
        AiDocumentMode.FromArticle => "from_article",
        AiDocumentMode.ResearchUrl => "research_url",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown powered_document mode.")
    };

    public static string DisplayName(this AiDocumentMode mode) => mode switch
    {
        AiDocumentMode.Article => "From a topic",
        AiDocumentMode.FromList => "From one of my lists",
        AiDocumentMode.FromArticle => "From one of my documents",
        AiDocumentMode.ResearchUrl => "From a URL",
        _ => mode.ToString()
    };
}

public static class AiWritingAssistActions
{
    public static string ToWire(this AiWritingAssistAction action) => action switch
    {
        AiWritingAssistAction.Rewrite => "rewrite",
        AiWritingAssistAction.Tighten => "tighten",
        AiWritingAssistAction.Expand => "expand",
        AiWritingAssistAction.Grammar => "grammar",
        AiWritingAssistAction.Thread => "thread",
        AiWritingAssistAction.Tags => "tags",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown writing_assist action.")
    };

    /// <summary>
    /// Which artifact kind this action comes back as. Verified live 2026-09-16
    /// for rewrite → "message" and thread → "thread"; tags → "tags" is
    /// documented but was not spent quota on.
    /// </summary>
    public static AiArtifactKind ExpectedKind(this AiWritingAssistAction action) => action switch
    {
        AiWritingAssistAction.Thread => AiArtifactKind.Thread,
        AiWritingAssistAction.Tags => AiArtifactKind.Tags,
        _ => AiArtifactKind.Message
    };
}

/// <summary>
/// The cross-post channel labels message_series accepts in context.channels.
/// They only size the generated messages to the tightest selected service's
/// character limit — the actual cross-posting is configured at /generate time
/// via <see cref="AiCrossPostOptions"/>.
/// </summary>
public static class AiSeriesChannels
{
    public const string Bluesky = "Bluesky";
    public const string Mastodon = "Mastodon";
    public const string LinkedIn = "LinkedIn";
    public const string Twitter = "X/Twitter";

    public static IReadOnlyList<string> All { get; } = new[] { Bluesky, Mastodon, LinkedIn, Twitter };
}

/// <summary>
/// The optional, id-based "context" object on POST /api/ai/suggest. Fields are
/// feature-specific and unknown fields are ignored server-side; numeric hints are
/// clamped. Build one with the static factories so the required companion field
/// for a derived powered_document mode can't be forgotten — a missing (or
/// not-yours) reference is 422 invalid_input, and a failed call still burns a
/// quota unit.
///
/// Deliberately omitted: context.spacingMinutes. The API accepts it for forward
/// compatibility but documents it as ignored (message_series rows are always
/// spaced a fixed 4 minutes apart), so exposing it would only mislead a caller.
/// </summary>
public sealed class AiContext
{
    /// <summary>powered_template: which base template to personalize.</summary>
    public string? TemplateKey { get; init; }

    /// <summary>powered_document: article (default) | from_list | from_article | research_url.</summary>
    public AiDocumentMode? Mode { get; init; }

    /// <summary>powered_document (from_list). Must be a list you own.</summary>
    public string? ListId { get; init; }

    /// <summary>powered_document (from_article). Must be a document you own.</summary>
    public string? DocumentId { get; init; }

    /// <summary>powered_document (research_url). http/https only.</summary>
    public string? Url { get; init; }

    /// <summary>writing_assist: rewrite (default) | tighten | expand | grammar | thread | tags.</summary>
    public AiWritingAssistAction? Action { get; init; }

    /// <summary>writing_assist: platform hint (e.g. "mastodon") used as a character-limit target.</summary>
    public string? TargetPlatform { get; init; }

    /// <summary>message_series (3–12, default 5) / article_series (2–6, default 4). Clamped both sides.</summary>
    public int? Count { get; init; }

    /// <summary>message_series: selected composer channels — see <see cref="AiSeriesChannels"/>.</summary>
    public List<string>? Channels { get; init; }

    // ── Factories ───────────────────────────────────────────────────────────────

    public static AiContext PoweredTemplate(string? templateKey = null)
        => new() { TemplateKey = templateKey };

    public static AiContext DocumentFromTopic()
        => new() { Mode = AiDocumentMode.Article };

    public static AiContext DocumentFromList(string listId)
        => new() { Mode = AiDocumentMode.FromList, ListId = Require(listId, nameof(listId)) };

    public static AiContext DocumentFromArticle(string documentId)
        => new() { Mode = AiDocumentMode.FromArticle, DocumentId = Require(documentId, nameof(documentId)) };

    public static AiContext DocumentFromResearchUrl(string url)
        => new() { Mode = AiDocumentMode.ResearchUrl, Url = Require(url, nameof(url)) };

    public static AiContext WritingAssist(AiWritingAssistAction action, string? targetPlatform = null)
        => new() { Action = action, TargetPlatform = targetPlatform };

    public static AiContext MessageSeries(int? count = null, IEnumerable<string>? channels = null)
        => new() { Count = count, Channels = channels?.ToList() };

    public static AiContext ArticleSeries(int? count = null)
        => new() { Count = count };

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required for this powered_document mode.", name)
            : value;

    // ── Validation / serialization ──────────────────────────────────────────────

    /// <summary>
    /// Client-side pre-flight for the rules the server enforces with 422
    /// invalid_input. Returns false with a human message rather than throwing so
    /// the caller decides (the client throws an
    /// <see cref="InterlinedList.Services.AiApiException"/> with code
    /// invalid_input, matching what the server would have said).
    /// </summary>
    public bool TryValidate(AiFeature feature, out string? error)
    {
        error = null;

        if (feature == AiFeature.PoweredDocument && Mode is { } mode)
        {
            switch (mode)
            {
                case AiDocumentMode.FromList when string.IsNullOrWhiteSpace(ListId):
                    error = "Powered Document mode from_list requires context.listId.";
                    return false;
                case AiDocumentMode.FromArticle when string.IsNullOrWhiteSpace(DocumentId):
                    error = "Powered Document mode from_article requires context.documentId.";
                    return false;
                case AiDocumentMode.ResearchUrl when string.IsNullOrWhiteSpace(Url):
                    error = "Powered Document mode research_url requires context.url.";
                    return false;
                case AiDocumentMode.ResearchUrl
                    when !Uri.TryCreate(Url, UriKind.Absolute, out var uri)
                         || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps):
                    error = "context.url must be an absolute http or https URL.";
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The wire object, carrying only the fields that were actually set — the
    /// shared JsonSerializerOptions don't drop nulls, and a null "mode"/"action"
    /// would be a set-but-empty field rather than an absent one.
    /// </summary>
    public Dictionary<string, object?> ToWire(AiFeature feature)
    {
        var wire = new Dictionary<string, object?>();

        if (TemplateKey is { Length: > 0 }) wire["templateKey"] = TemplateKey;
        if (Mode is { } mode) wire["mode"] = mode.ToWire();
        if (ListId is { Length: > 0 }) wire["listId"] = ListId;
        if (DocumentId is { Length: > 0 }) wire["documentId"] = DocumentId;
        if (Url is { Length: > 0 }) wire["url"] = Url;
        if (Action is { } action) wire["action"] = action.ToWire();
        if (TargetPlatform is { Length: > 0 }) wire["targetPlatform"] = TargetPlatform;
        if (Count is { } count) wire["count"] = AiFeatureLimits.ClampItemCount(feature, count);
        if (Channels is { Count: > 0 }) wire["channels"] = Channels;

        return wire;
    }
}
