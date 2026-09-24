using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// How an <see cref="AiNotice"/> should read and look. Kept distinct from
/// <see cref="AiErrorCode"/> because several codes collapse to the same
/// treatment while two of them — <see cref="QuotaExceeded"/> and
/// <see cref="RateLimited"/> — must stay visibly different from each other and
/// from a generic failure. That's the whole point of #10's last two acceptance
/// criteria.
/// </summary>
public enum AiNoticeKind
{
    /// <summary>Neutral progress/result copy ("Draft ready — review it below").</summary>
    Info,

    /// <summary>The user's input needs changing before it's worth spending a unit. Actionable, their side.</summary>
    Input,

    /// <summary>429 quota_exceeded — today's 50 are gone. Comes back tomorrow, not sooner.</summary>
    QuotaExceeded,

    /// <summary>429 rate_limited — the 15/60s window tripped. Comes back in seconds.</summary>
    RateLimited,

    /// <summary>422 invalid_ai_output / refused — the model answered unusably. A unit was still spent.</summary>
    ModelDeclined,

    /// <summary>Everything else: provider_error, 401, an unknown code, a transport failure.</summary>
    Error
}

/// <summary>
/// One non-blocking, in-place message from an AI call. Rendered as a line of
/// text inside the panel that raised it — never a <c>MessageBox</c>, never
/// anything that blocks the dispatcher (#10).
///
/// <see cref="Spent"/> exists because this API's quota accounting is
/// asymmetric: an input-validation rejection costs nothing (verified live —
/// <c>usedToday</c> was unchanged across four of them), but any call that
/// reaches the model costs one of the 50 even when it fails. A user who just
/// lost a unit to <c>invalid_ai_output</c> deserves to be told so, and told to
/// re-word rather than mash the button.
/// </summary>
public sealed record AiNotice(AiNoticeKind Kind, string Text, bool Spent = false)
{
    /// <summary>True for the states worth an amber (live/pending) treatment rather than red.</summary>
    public bool IsTransient => Kind is AiNoticeKind.RateLimited or AiNoticeKind.QuotaExceeded or AiNoticeKind.ModelDeclined;

    public bool IsQuotaExceeded => Kind == AiNoticeKind.QuotaExceeded;
    public bool IsRateLimited => Kind == AiNoticeKind.RateLimited;

    public static AiNotice Info(string text) => new(AiNoticeKind.Info, text);

    /// <summary>A client-side pre-flight rejection. No unit spent — that's the reason pre-flight exists.</summary>
    public static AiNotice Input(string text) => new(AiNoticeKind.Input, text, Spent: false);

    /// <summary>
    /// Map a failed AI call onto its message. Branches on
    /// <see cref="AiApiException.Code"/> only — never on the server's prose —
    /// except for <c>invalid_input</c>, where the server's wording is specific
    /// and better than anything generic ("List not found.", "A list must be
    /// selected.", "Only http(s) URLs are supported." — all verified live).
    /// </summary>
    public static AiNotice From(AiApiException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex.Code switch
        {
            AiErrorCode.QuotaExceeded => new(
                AiNoticeKind.QuotaExceeded,
                $"Today's AI allowance is used up ({AiFeatureLimits.DailyGenerationLimit} per rolling 24 hours). " +
                "It frees up again as the oldest of today's requests ages out — try again tomorrow.",
                Spent: false),

            AiErrorCode.RateLimited => new(
                AiNoticeKind.RateLimited,
                ex.RetryAfter is { } wait
                    ? $"Too many AI requests in the last minute. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} seconds."
                    : $"Too many AI requests in the last minute (the limit is {AiFeatureLimits.RateLimitRequestsPerMinute}). Wait a moment and try again.",
                Spent: false),

            // Both of these got as far as the model, so a unit is gone. Say so:
            // it's the difference between "try again" and "try again, but
            // change something first".
            AiErrorCode.InvalidAiOutput => new(
                AiNoticeKind.ModelDeclined,
                "The AI's answer came back in a shape this app couldn't use. That attempt still counted against today's allowance — re-word the request before trying again.",
                Spent: true),

            AiErrorCode.Refused => new(
                AiNoticeKind.ModelDeclined,
                "The AI declined to answer that. That attempt still counted against today's allowance — try different wording.",
                Spent: true),

            // The server's own message is the useful one here, and a local
            // pre-flight rejection reuses the same code with nothing spent.
            AiErrorCode.InvalidInput => new(AiNoticeKind.Input, ex.UserMessage, Spent: false),

            AiErrorCode.ProviderError => new(
                AiNoticeKind.Error,
                "The AI provider didn't answer. That attempt may still have counted against today's allowance — give it a minute before trying again.",
                Spent: true),

            _ => new(AiNoticeKind.Error, ex.UserMessage, Spent: !ex.IsLocal)
        };
    }
}
