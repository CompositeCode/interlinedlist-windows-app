namespace InterlinedList.Services;

/// <summary>
/// The distinct ways POST /api/materialize fails. Classified from the HTTP status
/// because that's what actually discriminates: verified live 2026-09-16, the
/// endpoint returns one error <c>code</c> per status
/// (<c>401 unauthorized</c> · <c>400 bad_request</c> · <c>404 not_found</c> ·
/// <c>500 internal_error</c>), so the status carries all the signal and nothing
/// depends on <see cref="InterlinedApiException"/> growing a code property.
/// </summary>
public enum MaterializeFailure
{
    Unknown,

    /// <summary><c>401</c> — no/expired credentials.</summary>
    Unauthenticated,

    /// <summary>
    /// <c>403</c> — authenticated but not a subscriber. "Create from…" is gated the
    /// same way list/document creation is; surface an upgrade prompt, not an error.
    /// </summary>
    SubscriptionRequired,

    /// <summary>
    /// <c>400</c> — missing source, bad field config, or an over-limit draft body
    /// without <c>allowThread</c>. The server's message is written for humans, so
    /// prefer showing it verbatim.
    /// </summary>
    InvalidRequest,

    /// <summary><c>404</c> — a referenced message/list/row/document is gone or was never yours.</summary>
    NotFoundOrNotOwned,

    /// <summary><c>5xx</c>.</summary>
    ServerError
}

/// <summary>
/// Maps a materialize <see cref="InterlinedApiException"/> to a
/// <see cref="MaterializeFailure"/> so callers can branch on the upgrade-needed and
/// not-found cases instead of showing one generic error.
/// </summary>
public static class MaterializeError
{
    public static MaterializeFailure Classify(InterlinedApiException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return Classify(ex.StatusCode);
    }

    public static MaterializeFailure Classify(int statusCode) => statusCode switch
    {
        400 => MaterializeFailure.InvalidRequest,
        401 => MaterializeFailure.Unauthenticated,
        403 => MaterializeFailure.SubscriptionRequired,
        404 => MaterializeFailure.NotFoundOrNotOwned,
        >= 500 and < 600 => MaterializeFailure.ServerError,
        _ => MaterializeFailure.Unknown
    };

    /// <summary>True when the only fix is a subscription — show an upgrade affordance.</summary>
    public static bool IsSubscriptionRequired(InterlinedApiException ex) =>
        Classify(ex) == MaterializeFailure.SubscriptionRequired;

    /// <summary>
    /// A display string for a failed materialize. <c>400</c> keeps the server's own
    /// wording — it is already user-facing and specific (e.g. "This content is 1436
    /// characters, over the 666-character limit. Shorten it, or set allowThread to
    /// post it as a thread."), so paraphrasing it would only lose detail.
    /// </summary>
    public static string Describe(InterlinedApiException ex) => Classify(ex) switch
    {
        MaterializeFailure.Unauthenticated =>
            "Your session has expired. Sign in again and retry.",
        MaterializeFailure.SubscriptionRequired =>
            "Create from… is a subscriber feature. Upgrade your plan to turn messages, lists and documents into new lists or documents.",
        MaterializeFailure.NotFoundOrNotOwned =>
            $"Something you selected is no longer available, or isn't yours. ({ex.Message})",
        MaterializeFailure.ServerError =>
            $"The server couldn't complete this conversion. ({ex.Message})",
        _ => ex.Message
    };
}
