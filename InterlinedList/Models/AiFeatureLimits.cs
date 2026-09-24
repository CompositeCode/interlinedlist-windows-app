namespace InterlinedList.Models;

/// <summary>
/// The per-feature ceilings the server enforces, mirrored client-side. Worth
/// mirroring because a rejected call still consumes one of the 50 daily
/// generations — the cheapest place to catch "too long" or "under the 10-word
/// gate" is before the request leaves.
///
/// Source: the published contract at /help/api/ai-integration. maxOutputTokens
/// is clamped *down* to the ceiling server-side and never up.
/// </summary>
public sealed record AiFeatureLimit(
    int MaxInputWords,
    int MaxOutputTokens,
    int MinInputWords,
    int MinItems,
    int MaxItems,
    int DefaultItems);

public static class AiFeatureLimits
{
    public static AiFeatureLimit For(AiFeature feature) => feature switch
    {
        // Series features additionally require ≥10 words of input (the composer's gate).
        AiFeature.WritingAssist => new(MaxInputWords: 1500, MaxOutputTokens: 1024, MinInputWords: 1, MinItems: 0, MaxItems: 0, DefaultItems: 0),
        AiFeature.PoweredTemplate => new(MaxInputWords: 300, MaxOutputTokens: 4096, MinInputWords: 1, MinItems: 0, MaxItems: 0, DefaultItems: 0),
        AiFeature.MessageSeries => new(MaxInputWords: 500, MaxOutputTokens: 4096, MinInputWords: 10, MinItems: 3, MaxItems: 12, DefaultItems: 5),
        AiFeature.PoweredDocument => new(MaxInputWords: 500, MaxOutputTokens: 8000, MinInputWords: 1, MinItems: 0, MaxItems: 0, DefaultItems: 0),
        AiFeature.ArticleSeries => new(MaxInputWords: 500, MaxOutputTokens: 8000, MinInputWords: 10, MinItems: 2, MaxItems: 6, DefaultItems: 4),
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown AI feature.")
    };

    /// <summary>Daily quota: 50 AI generations per rolling 24 hours, per user. Both phases count.</summary>
    public const int DailyGenerationLimit = 50;

    /// <summary>Guaranteed floor of the short-window limiter: 15 requests / 60 seconds, per user.</summary>
    public const int RateLimitRequestsPerMinute = 15;

    /// <summary>A generated document is capped at 40,000 characters server-side.</summary>
    public const int MaxDocumentCharacters = 40_000;

    /// <summary>A single generated message is capped at 3,000 characters server-side.</summary>
    public const int MaxMessageCharacters = 3_000;

    /// <summary>Whitespace-separated word count, matching how the word caps read.</summary>
    public static int CountWords(string? input)
        => string.IsNullOrWhiteSpace(input)
            ? 0
            : input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Pre-flight the input against this feature's caps. Returns false with the
    /// message the server would have sent (422 invalid_input) rather than
    /// throwing, so the client can raise its own typed exception.
    /// </summary>
    public static bool TryValidateInput(AiFeature feature, string? input, out string? error)
    {
        error = null;
        var limit = For(feature);
        var words = CountWords(input);

        if (words == 0)
        {
            error = "Some input is required.";
            return false;
        }

        if (words < limit.MinInputWords)
        {
            error = $"A basic amount of content is required (at least {limit.MinInputWords} words).";
            return false;
        }

        if (words > limit.MaxInputWords)
        {
            error = $"{feature.DisplayName()} accepts at most {limit.MaxInputWords} words (got {words}).";
            return false;
        }

        return true;
    }

    /// <summary>Clamp a requested output budget into this feature's ceiling (the server clamps down too).</summary>
    public static int ClampOutputTokens(AiFeature feature, int requested)
        => Math.Clamp(requested, 1, For(feature).MaxOutputTokens);

    /// <summary>Clamp a requested context.count into the feature's series bounds. Non-series features pass through.</summary>
    public static int ClampItemCount(AiFeature feature, int requested)
    {
        var limit = For(feature);
        return limit.MaxItems <= 0 ? requested : Math.Clamp(requested, limit.MinItems, limit.MaxItems);
    }
}
