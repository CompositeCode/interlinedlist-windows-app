using System.Net;

namespace InterlinedList.Services;

/// <summary>
/// An error response from the InterlinedList API.
/// </summary>
/// <remarks>
/// Every error body on this backend carries a stable machine-readable
/// <c>code</c> alongside the human-readable <c>error</c> string, e.g.
/// <c>{ "error": "Not found", "code": "not_found" }</c>. Branch on
/// <see cref="Code"/> (or the predicates below) rather than string-matching
/// <see cref="Exception.Message"/> — the prose is not a contract, the code is.
/// </remarks>
public sealed class InterlinedApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>
    /// The API's stable machine-readable error code, when the body carried one:
    /// <c>not_found</c>, <c>bad_request</c>, <c>unauthorized</c>,
    /// <c>subscription_required</c>, <c>quota_exceeded</c>, <c>rate_limited</c>,
    /// <c>version_conflict</c>, <c>payload_too_large</c>, <c>invalid_input</c>,
    /// <c>invalid_ai_output</c>, <c>no_provider_configured</c>,
    /// <c>provider_error</c>, <c>missing_handle</c>, … Null when absent.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// The raw response body. Needed for the compare-and-swap rebase on
    /// <c>409 version_conflict</c>, where the body carries the full current
    /// document under <c>current</c>.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>How long to wait before retrying, from the <c>Retry-After</c> header.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>The <c>RateLimit-Limit</c> header, when present.</summary>
    public int? RateLimitLimit { get; init; }

    /// <summary>The <c>RateLimit-Remaining</c> header, when present.</summary>
    public int? RateLimitRemaining { get; init; }

    /// <summary>The <c>RateLimit-Reset</c> header, when present.</summary>
    public string? RateLimitReset { get; init; }

    public InterlinedApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    // ── Classification ──────────────────────────────────────────────────────────
    // Status alone isn't enough: 403 can be "not a subscriber" or an ordinary
    // authorization failure, and 429 can be a daily quota or a short-window
    // throttle. These read the code first and fall back to the status.

    /// <summary>
    /// The account needs an active subscription. Callers should render the shared
    /// "upgrade needed" state rather than a generic error — this is a normal,
    /// expected outcome for a free account, not a failure.
    /// </summary>
    public bool IsSubscriptionRequired =>
        Code == "subscription_required" || StatusCode == (int)HttpStatusCode.PaymentRequired;

    /// <summary>A daily quota is exhausted (e.g. the 50/day AI cap). Retrying sooner won't help.</summary>
    public bool IsQuotaExceeded => Code == "quota_exceeded";

    /// <summary>A short-window rate limit tripped. Honor <see cref="RetryAfter"/>.</summary>
    public bool IsRateLimited =>
        Code == "rate_limited" || (StatusCode == 429 && !IsQuotaExceeded);

    /// <summary>Optimistic-concurrency failure; rebase onto <c>current</c> in <see cref="ResponseBody"/> and retry.</summary>
    public bool IsVersionConflict =>
        Code == "version_conflict" || StatusCode == (int)HttpStatusCode.Conflict;

    public bool IsNotFound =>
        Code == "not_found" || StatusCode == (int)HttpStatusCode.NotFound;

    public bool IsUnauthorized =>
        Code == "unauthorized" || StatusCode == (int)HttpStatusCode.Unauthorized;

    public bool IsPayloadTooLarge =>
        Code == "payload_too_large" || StatusCode == 413;

    /// <summary>
    /// The server has no AI provider configured. A site misconfiguration, not
    /// something the client can fix — hide the feature rather than erroring.
    /// </summary>
    public bool IsNoProviderConfigured => Code == "no_provider_configured";
}
