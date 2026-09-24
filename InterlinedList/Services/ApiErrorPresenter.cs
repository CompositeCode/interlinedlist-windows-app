using System.Net.Http;

namespace InterlinedList.Services;

/// <summary>
/// Turns an API failure into something a view can show, so every gated feature
/// gets the same treatment instead of each ViewModel inventing its own.
/// </summary>
/// <remarks>
/// The important case is <c>402</c>/<c>403 subscription_required</c>. On this
/// backend that is a <b>normal outcome for a free account</b>, not a failure:
/// creating lists, folders, documents and organizations, attaching media,
/// cross-posting, scheduling, sharing with people, and every AI action are all
/// subscriber-gated. Rendering those as a generic red error is the single
/// easiest way to make the app feel broken when it is working correctly.
/// </remarks>
public static class ApiErrorPresenter
{
    /// <summary>
    /// A user-facing description of a failure, plus the kind so the view can
    /// choose its treatment (upgrade prompt vs retry hint vs plain error).
    /// </summary>
    /// <param name="Kind">How the view should present this.</param>
    /// <param name="Message">Text to show the user.</param>
    /// <param name="RetryAfter">When <see cref="ApiErrorKind.RateLimited"/>, how long to wait.</param>
    public readonly record struct Presentation(
        ApiErrorKind Kind,
        string Message,
        TimeSpan? RetryAfter = null);

    public static Presentation Describe(Exception ex) => ex switch
    {
        InterlinedApiException api => DescribeApi(api),
        TaskCanceledException => new(ApiErrorKind.Network, "The request timed out. Check your connection and try again."),
        HttpRequestException => new(ApiErrorKind.Network, "Couldn't reach InterlinedList. Check your connection and try again."),
        _ => new(ApiErrorKind.Error, ex.Message),
    };

    private static Presentation DescribeApi(InterlinedApiException ex)
    {
        if (ex.IsSubscriptionRequired)
            return new(ApiErrorKind.UpgradeRequired,
                "This is a Subscriber feature. Open Settings → Subscription to upgrade.");

        if (ex.IsQuotaExceeded)
            return new(ApiErrorKind.QuotaExceeded,
                "You've used today's allowance for this feature. It resets tomorrow.");

        if (ex.IsRateLimited)
        {
            var wait = ex.RetryAfter;
            var when = wait is { } w && w > TimeSpan.Zero
                ? $" Try again in {Describe(w)}."
                : " Try again shortly.";
            return new(ApiErrorKind.RateLimited, $"Too many requests.{when}", wait);
        }

        if (ex.IsNoProviderConfigured)
            return new(ApiErrorKind.Unavailable,
                "This feature isn't configured on the server right now.");

        if (ex.IsUnauthorized)
            return new(ApiErrorKind.Unauthorized,
                "Your session isn't valid any more. Sign in again.");

        if (ex.IsNotFound)
            return new(ApiErrorKind.NotFound, "That item no longer exists, or isn't yours.");

        if (ex.IsPayloadTooLarge)
            return new(ApiErrorKind.Error, "That's too large to upload.");

        // A plain 403 that isn't a subscription gate is a real authorization
        // failure — don't mislabel it as an upgrade prompt.
        if (ex.StatusCode == 403)
            return new(ApiErrorKind.Forbidden, "You don't have permission to do that.");

        return new(ApiErrorKind.Error, ex.Message);
    }

    private static string Describe(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => $"{Math.Max(1, (int)Math.Ceiling(span.TotalSeconds))} seconds",
        < 3600 => $"{(int)Math.Ceiling(span.TotalMinutes)} minutes",
        _ => $"{(int)Math.Ceiling(span.TotalHours)} hours",
    };

    /// <summary>
    /// True when the account cannot do this because of its tier — the caller
    /// should show an upgrade affordance, not an error.
    /// </summary>
    public static bool IsUpgradePrompt(Exception ex) =>
        ex is InterlinedApiException { IsSubscriptionRequired: true };

    // NOTE: account-status gating (probationary / restricted accounts, unverified
    // email) is a separate axis from tier gating and is handled where the account
    // status itself is modelled — see #50.
}

/// <summary>How a view should present an API failure.</summary>
public enum ApiErrorKind
{
    /// <summary>Generic failure — show the message.</summary>
    Error,

    /// <summary>Needs a subscription. Show an upgrade affordance, not an error.</summary>
    UpgradeRequired,

    /// <summary>A daily allowance is spent. Retrying sooner won't help.</summary>
    QuotaExceeded,

    /// <summary>Short-window throttle. Honor the retry delay.</summary>
    RateLimited,

    /// <summary>Server-side feature not configured. Hide rather than error.</summary>
    Unavailable,

    /// <summary>Session no longer valid — re-authenticate.</summary>
    Unauthorized,

    /// <summary>Real authorization failure (not a tier gate).</summary>
    Forbidden,

    /// <summary>Gone, or never visible to this account.</summary>
    NotFound,

    /// <summary>Couldn't reach the server.</summary>
    Network,
}
