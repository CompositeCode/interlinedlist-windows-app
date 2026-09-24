namespace InterlinedList.Models;

/// <summary>
/// GET /api/ai/status — whether the site has AI configured, whether this account
/// is a subscriber, the per-provider fallback models, and the remaining daily
/// quota. Never returns keys. Any authenticated user may read it (subscriber
/// gating applies to /suggest and /generate, not here).
///
/// Verified live 2026-09-16 with a bearer sync-token, byte-for-byte:
/// {"subscriber":true,"providers":["anthropic"],"defaultModels":{"anthropic":
/// "claude-sonnet-5","openai":"gpt-4.1-mini","gemini":"gemini-2.0-flash"},
/// "quota":{"usedToday":0,"dailyLimit":50,"remaining":50}}
/// </summary>
public sealed class AiStatus
{
    public const string AnthropicProvider = "anthropic";

    /// <summary>Whether this account may call /suggest and /generate (else 403 subscription_required).</summary>
    public bool Subscriber { get; init; }

    /// <summary>
    /// Providers the *site* has configured: ["anthropic"] when the server key is
    /// set, [] when it isn't. Empty means /suggest and /generate will answer
    /// 409 no_provider_configured.
    /// </summary>
    public List<string> Providers { get; init; } = new();

    /// <summary>
    /// Fallback model per provider id. Only the "anthropic" entry is reachable —
    /// the openai/gemini adapters are dormant in the server codebase and a
    /// "provider" request field is ignored (per-user provider keys were removed
    /// 2026-09-05), so don't offer a provider picker off the back of this.
    /// </summary>
    public Dictionary<string, string> DefaultModels { get; init; } = new();

    public AiQuota Quota { get; init; } = new();

    public bool HasProviderConfigured => Providers.Count > 0;

    /// <summary>The model /suggest will use when the request doesn't override it.</summary>
    public string? DefaultModel =>
        DefaultModels.TryGetValue(AnthropicProvider, out var model) && model.Length > 0 ? model : null;

    /// <summary>
    /// True when an AI action should be offered at all. False here maps to a
    /// specific reason — see <see cref="UnavailableReason"/>.
    /// </summary>
    public bool CanUseAi => Subscriber && HasProviderConfigured && !Quota.IsExhausted;

    /// <summary>Human-readable reason AI is unavailable, or null when it is available.</summary>
    public string? UnavailableReason =>
        !HasProviderConfigured ? "AI isn't configured on the server right now."
        : !Subscriber ? "AI features are available to subscribers."
        : Quota.IsExhausted ? $"Daily AI limit reached ({Quota.DailyLimit} per 24 hours)."
        : null;
}
