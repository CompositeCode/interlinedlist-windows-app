using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// POST /api/ai/suggest, 200 OK. Writes nothing — this is the preview half of
/// the suggest → generate flow. It does spend one of the 50 daily generations.
///
/// Verified live 2026-09-16, whole envelope:
/// {"ok":true,"feature":"writing_assist","artifact":{"kind":"message","content":
/// "Shipped the new feature today — it's fast, sleek, and people are already
/// loving it! 🚀"},"usage":{"inputTokens":125,"outputTokens":40,"model":
/// "claude-sonnet-5"},"quota":{"usedToday":1,"dailyLimit":50}}
/// </summary>
public sealed class AiSuggestResult
{
    public bool Ok { get; init; }

    /// <summary>Raw wire feature echoed by the server.</summary>
    public required string Feature { get; init; }

    public required AiArtifact Artifact { get; init; }

    /// <summary>Token counts. Present on every verified 200; nullable defensively.</summary>
    public AiUsage? Usage { get; init; }

    /// <summary>Quota after this call. Note: no "remaining" here — see <see cref="AiQuota"/>.</summary>
    public required AiQuota Quota { get; init; }

    /// <summary>The whole 200 body, for diagnostics/logging.</summary>
    public required JsonElement Raw { get; init; }

    public AiFeature? KnownFeature => AiFeatures.TryParse(Feature, out var feature) ? feature : null;

    /// <summary>Whether this preview can be handed to POST /api/ai/generate at all.</summary>
    public bool CanGenerate => Ok && Artifact.IsPersistable;
}
