using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// A failed /api/ai/* call, carrying the machine-readable code the endpoints
/// document ({ "error", "code" }) plus the Retry-After hint the 429 rate-limit
/// response sends. Catch this instead of string-matching a message.
///
/// Why a separate type rather than <see cref="InterlinedApiException"/>: that
/// class is <c>sealed</c> and, on this branch, carries only StatusCode +
/// Message — a richer version with a first-class <c>Code</c> property is in
/// flight in another PR. This file deliberately doesn't touch it. FOLLOW-UP:
/// once <see cref="InterlinedApiException"/> exposes <c>Code</c>, this type
/// should either derive from it or fold into it, so a single catch covers AI and
/// non-AI calls alike. Until then the AI methods throw only this type, and
/// nothing else in the app throws it — so no existing catch site changes
/// behavior.
///
/// Also used for the client-side pre-flight rejections (see
/// <see cref="InvalidInput"/>): a request the server would certainly answer with
/// 422 invalid_input is failed locally with the same code, because a rejected
/// call still spends one of the 50 daily generations.
/// </summary>
public sealed class AiApiException : Exception
{
    /// <summary>HTTP status, or the status the server would have used for a local rejection.</summary>
    public int StatusCode { get; }

    public AiErrorCode Code { get; }

    /// <summary>The raw "code" string, preserved so an unrecognized future code isn't lost.</summary>
    public string RawCode { get; }

    /// <summary>From the Retry-After header on 429 rate_limited. Null otherwise.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>True when the failure never reached the server (so no quota was spent).</summary>
    public bool IsLocal { get; }

    public AiApiException(
        int statusCode,
        AiErrorCode code,
        string message,
        string? rawCode = null,
        TimeSpan? retryAfter = null,
        bool isLocal = false)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        RawCode = rawCode ?? code.ToWire();
        RetryAfter = retryAfter;
        IsLocal = isLocal;
    }

    /// <summary>Whether repeating the identical request could succeed (it still costs quota — don't auto-retry).</summary>
    public bool IsRetryable => Code.IsRetryable();

    /// <summary>
    /// Copy for the user. Prefers the code's canonical wording over the server's
    /// prose, except for invalid_input where the server's message is specific
    /// and useful (e.g. "A basic amount of content is required (at least 10
    /// words).", verified live).
    /// </summary>
    public string UserMessage => Code switch
    {
        AiErrorCode.InvalidInput when !string.IsNullOrWhiteSpace(Message) => Message,
        AiErrorCode.Unknown when !string.IsNullOrWhiteSpace(Message) => Message,
        AiErrorCode.RateLimited when RetryAfter is { } wait =>
            $"Too many AI requests just now — try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))}s.",
        _ => Code.DefaultMessage()
    };

    /// <summary>Build from a non-success response, parsing { error, code } and Retry-After.</summary>
    public static async Task<AiApiException> FromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(ct);

        var rawCode = (string?)null;
        var message = body;

        try
        {
            var root = AiJson.ParseLenient(body);
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                    rawCode = codeProp.GetString();
                if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
                    message = errorProp.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON (an edge/proxy error page, say) — keep the raw text.
        }

        var code = AiErrorCodes.Parse(rawCode);
        if (code == AiErrorCode.Unknown)
            code = InferFromStatus(status);

        if (string.IsNullOrWhiteSpace(message))
            message = code.DefaultMessage();

        return new AiApiException(status, code, message, rawCode, ReadRetryAfter(resp));
    }

    /// <summary>A rejection raised before the request is sent — same code the server would return.</summary>
    public static AiApiException InvalidInput(string message)
        => new(422, AiErrorCode.InvalidInput, message, isLocal: true);

    /// <summary>
    /// Fall back to the status when the body has no usable code — e.g. a 401 from
    /// an edge layer that never reached the route handler.
    /// </summary>
    private static AiErrorCode InferFromStatus(int status) => status switch
    {
        401 => AiErrorCode.Unauthorized,
        403 => AiErrorCode.SubscriptionRequired,
        409 => AiErrorCode.NoProviderConfigured,
        422 => AiErrorCode.InvalidInput,
        429 => AiErrorCode.QuotaExceeded,
        500 or 502 or 503 or 504 => AiErrorCode.ProviderError,
        _ => AiErrorCode.Unknown
    };

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage resp)
    {
        var retryAfter = resp.Headers.RetryAfter;
        if (retryAfter is null) return null;
        if (retryAfter.Delta is { } delta) return delta;
        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }
}
