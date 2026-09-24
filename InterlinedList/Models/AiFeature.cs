namespace InterlinedList.Models;

/// <summary>
/// The five AI features accepted by POST /api/ai/suggest and POST /api/ai/generate.
/// Anything else is rejected server-side with 422 invalid_input, so the wire
/// value is kept behind an enum rather than passed as a free string.
/// </summary>
public enum AiFeature
{
    /// <summary>Generate a personalized list (schema DSL + starter rows) from a description.</summary>
    PoweredTemplate,

    /// <summary>Draft a single markdown document. Four modes — see <see cref="AiDocumentMode"/>.</summary>
    PoweredDocument,

    /// <summary>Plan a threaded series of short messages.</summary>
    MessageSeries,

    /// <summary>Plan and write a coherent series of documents.</summary>
    ArticleSeries,

    /// <summary>Rewrite / tighten / expand / fix / thread / tag a composer draft.</summary>
    WritingAssist
}

public static class AiFeatures
{
    public static IReadOnlyList<AiFeature> All { get; } = new[]
    {
        AiFeature.PoweredTemplate,
        AiFeature.PoweredDocument,
        AiFeature.MessageSeries,
        AiFeature.ArticleSeries,
        AiFeature.WritingAssist
    };

    /// <summary>The snake_case value the API expects in the request body's "feature" field.</summary>
    public static string ToWire(this AiFeature feature) => feature switch
    {
        AiFeature.PoweredTemplate => "powered_template",
        AiFeature.PoweredDocument => "powered_document",
        AiFeature.MessageSeries => "message_series",
        AiFeature.ArticleSeries => "article_series",
        AiFeature.WritingAssist => "writing_assist",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown AI feature.")
    };

    public static bool TryParse(string? wire, out AiFeature feature)
    {
        switch (wire)
        {
            case "powered_template": feature = AiFeature.PoweredTemplate; return true;
            case "powered_document": feature = AiFeature.PoweredDocument; return true;
            case "message_series": feature = AiFeature.MessageSeries; return true;
            case "article_series": feature = AiFeature.ArticleSeries; return true;
            case "writing_assist": feature = AiFeature.WritingAssist; return true;
            default: feature = default; return false;
        }
    }

    /// <summary>Short label for menus/buttons.</summary>
    public static string DisplayName(this AiFeature feature) => feature switch
    {
        AiFeature.PoweredTemplate => "Powered Template",
        AiFeature.PoweredDocument => "Powered Document",
        AiFeature.MessageSeries => "Message Series",
        AiFeature.ArticleSeries => "Article Series",
        AiFeature.WritingAssist => "Writing Assistance",
        _ => feature.ToString()
    };

    /// <summary>
    /// True when a successful /suggest for this feature yields an artifact that
    /// POST /api/ai/generate will persist. Only writing_assist is purely
    /// client-side (its message/thread/tags artifacts return 422 invalid_input
    /// from /generate — they're meant to be inserted into the composer).
    /// </summary>
    public static bool ProducesPersistableArtifact(this AiFeature feature)
        => feature != AiFeature.WritingAssist;
}
