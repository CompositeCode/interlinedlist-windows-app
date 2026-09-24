namespace InterlinedList.Models;

/// <summary>
/// The "kind" discriminator on an AI artifact envelope. Which kinds a feature can
/// produce is fixed (see <see cref="AiArtifactKinds.KindsFor"/>), and three of them
/// — <see cref="Message"/>, <see cref="Thread"/>, <see cref="Tags"/> — are
/// <b>not persistable</b>: POST /api/ai/generate answers 422 invalid_input for
/// them because they're composer insertions, not stored content.
/// </summary>
public enum AiArtifactKind
{
    /// <summary>powered_template → a list (schema DSL + starter rows). Persistable.</summary>
    List,

    /// <summary>powered_document → a single markdown document. Persistable.</summary>
    Document,

    /// <summary>message_series → an ordered set of short messages. Persistable.</summary>
    MessageSeries,

    /// <summary>article_series → a folder of documents. Persistable.</summary>
    DocSeries,

    /// <summary>writing_assist → a single rewritten draft. NOT persistable.</summary>
    Message,

    /// <summary>writing_assist → a draft split into thread parts. NOT persistable.</summary>
    Thread,

    /// <summary>writing_assist → suggested tags. NOT persistable.</summary>
    Tags
}

public static class AiArtifactKinds
{
    public static string ToWire(this AiArtifactKind kind) => kind switch
    {
        AiArtifactKind.List => "list",
        AiArtifactKind.Document => "document",
        AiArtifactKind.MessageSeries => "message_series",
        AiArtifactKind.DocSeries => "doc_series",
        AiArtifactKind.Message => "message",
        AiArtifactKind.Thread => "thread",
        AiArtifactKind.Tags => "tags",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown AI artifact kind.")
    };

    public static bool TryParse(string? wire, out AiArtifactKind kind)
    {
        switch (wire)
        {
            case "list": kind = AiArtifactKind.List; return true;
            case "document": kind = AiArtifactKind.Document; return true;
            case "message_series": kind = AiArtifactKind.MessageSeries; return true;
            case "doc_series": kind = AiArtifactKind.DocSeries; return true;
            case "message": kind = AiArtifactKind.Message; return true;
            case "thread": kind = AiArtifactKind.Thread; return true;
            case "tags": kind = AiArtifactKind.Tags; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>
    /// Whether POST /api/ai/generate will accept this artifact. The three
    /// writing_assist kinds are documented as non-persistable (422 invalid_input);
    /// <see cref="InterlinedList.Services.InterlinedApiClient.AiGenerateAsync"/>
    /// refuses them client-side so a caller never spends quota learning that.
    /// </summary>
    public static bool IsPersistable(this AiArtifactKind kind) => kind
        is AiArtifactKind.List
        or AiArtifactKind.Document
        or AiArtifactKind.MessageSeries
        or AiArtifactKind.DocSeries;

    /// <summary>The artifact kinds a given feature's /suggest call can return.</summary>
    public static IReadOnlyList<AiArtifactKind> KindsFor(AiFeature feature) => feature switch
    {
        AiFeature.PoweredTemplate => new[] { AiArtifactKind.List },
        AiFeature.PoweredDocument => new[] { AiArtifactKind.Document },
        AiFeature.MessageSeries => new[] { AiArtifactKind.MessageSeries },
        AiFeature.ArticleSeries => new[] { AiArtifactKind.DocSeries },
        // Which of the three comes back depends on context.action — see
        // AiWritingAssistActions.ExpectedKind.
        AiFeature.WritingAssist => new[] { AiArtifactKind.Message, AiArtifactKind.Thread, AiArtifactKind.Tags },
        _ => Array.Empty<AiArtifactKind>()
    };

    /// <summary>True when <paramref name="kind"/> is one the feature is documented to produce.</summary>
    public static bool IsValidFor(this AiArtifactKind kind, AiFeature feature)
        => KindsFor(feature).Contains(kind);
}
