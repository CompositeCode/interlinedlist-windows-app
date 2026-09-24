namespace InterlinedList.Models;

/// <summary>
/// The machine-readable "code" every /api/ai/* error body carries:
/// { "error": "human message", "code": "machine_code" }. Verified live
/// 2026-09-16 — GET /api/ai/status unauthenticated returned
/// {"error":"Unauthorized","code":"unauthorized"} and POST /api/ai/suggest with
/// a bogus feature returned {"error":"Unknown or missing feature.","code":
/// "invalid_input"} — so branching on the code (never on the prose) is sound.
/// </summary>
public enum AiErrorCode
{
    /// <summary>Body had no recognizable code, or a code this client doesn't know yet.</summary>
    Unknown,

    /// <summary>401 — no valid session or bearer token.</summary>
    Unauthorized,

    /// <summary>403 — authenticated but not a subscriber.</summary>
    SubscriptionRequired,

    /// <summary>409 — the server has no ANTHROPIC_API_KEY. Site misconfiguration; not client-fixable.</summary>
    NoProviderConfigured,

    /// <summary>422 — bad request: unknown feature, empty/over-long input, series under the 10-word gate, missing source reference, or a non-persistable artifact on /generate.</summary>
    InvalidInput,

    /// <summary>422 — the model's output failed validation. Retry with different input.</summary>
    InvalidAiOutput,

    /// <summary>422 — the provider declined to answer. Treat like <see cref="InvalidAiOutput"/>.</summary>
    Refused,

    /// <summary>429 — the 50/day quota is spent (failed attempts count too).</summary>
    QuotaExceeded,

    /// <summary>429 — the 15/60s short-window limiter tripped. Honor Retry-After.</summary>
    RateLimited,

    /// <summary>500 or 502 — upstream provider failure/timeout (60s per call) or an unexpected server error. Both carry this one code.</summary>
    ProviderError
}

public static class AiErrorCodes
{
    public static AiErrorCode Parse(string? wire) => wire switch
    {
        "unauthorized" => AiErrorCode.Unauthorized,
        "subscription_required" => AiErrorCode.SubscriptionRequired,
        "no_provider_configured" => AiErrorCode.NoProviderConfigured,
        "invalid_input" => AiErrorCode.InvalidInput,
        "invalid_ai_output" => AiErrorCode.InvalidAiOutput,
        "refused" => AiErrorCode.Refused,
        "quota_exceeded" => AiErrorCode.QuotaExceeded,
        "rate_limited" => AiErrorCode.RateLimited,
        "provider_error" => AiErrorCode.ProviderError,
        _ => AiErrorCode.Unknown
    };

    public static string ToWire(this AiErrorCode code) => code switch
    {
        AiErrorCode.Unauthorized => "unauthorized",
        AiErrorCode.SubscriptionRequired => "subscription_required",
        AiErrorCode.NoProviderConfigured => "no_provider_configured",
        AiErrorCode.InvalidInput => "invalid_input",
        AiErrorCode.InvalidAiOutput => "invalid_ai_output",
        AiErrorCode.Refused => "refused",
        AiErrorCode.QuotaExceeded => "quota_exceeded",
        AiErrorCode.RateLimited => "rate_limited",
        AiErrorCode.ProviderError => "provider_error",
        _ => "unknown"
    };

    /// <summary>
    /// Whether the same request could plausibly succeed if repeated. Note the
    /// quota counts failed attempts, so "retryable" still costs — a UI should
    /// make the user ask again rather than retry on its own.
    /// </summary>
    public static bool IsRetryable(this AiErrorCode code) => code
        is AiErrorCode.RateLimited
        or AiErrorCode.ProviderError
        or AiErrorCode.InvalidAiOutput
        or AiErrorCode.Refused;

    /// <summary>Default user-facing copy per code. (The AI analogue of ApiErrorPresenter.)</summary>
    public static string DefaultMessage(this AiErrorCode code) => code switch
    {
        AiErrorCode.Unauthorized => "Your session has expired — sign in again.",
        AiErrorCode.SubscriptionRequired => "AI features are available to subscribers.",
        AiErrorCode.NoProviderConfigured => "AI isn't configured on the server right now.",
        AiErrorCode.InvalidInput => "That request couldn't be used as-is — check the draft and try again.",
        AiErrorCode.InvalidAiOutput => "The AI's response couldn't be used. Try again with different wording.",
        AiErrorCode.Refused => "The AI declined to answer that. Try different wording.",
        AiErrorCode.QuotaExceeded => $"Daily AI limit reached ({AiFeatureLimits.DailyGenerationLimit} per 24 hours).",
        AiErrorCode.RateLimited => "Too many AI requests just now — wait a moment and try again.",
        AiErrorCode.ProviderError => "The AI provider didn't respond. Try again in a minute.",
        _ => "The AI request failed."
    };
}
