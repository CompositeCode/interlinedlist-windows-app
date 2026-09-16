using System.Net.Http;

namespace InterlinedList.Services;

/// <summary>
/// Pre-login self-service: account registration and password reset. All of
/// these are public (OpenAPI <c>security: []</c>) — no bearer token is needed,
/// and sending one changes nothing, confirmed live 2026-09-16 by issuing each
/// call twice, with and without a valid sync token, and getting byte-identical
/// responses. Request shapes verified against the OpenAPI spec and
/// <c>/help/api/authentication</c>: register { email, username, password,
/// displayName }, forgot { email }, reset { token, password }.
/// Responses aren't parsed — on success the caller either logs in with the new
/// credentials or tells the user to check their inbox.
/// </summary>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// The server's minimum password length, learned from its own validation
    /// message ("Password must be at least 10 characters", live 2026-09-16).
    /// Callers pre-check against this because the server validates password
    /// length <b>before</b> the reset token, so a too-short password hides an
    /// invalid-token error and the user fixes the wrong thing.
    /// </summary>
    public const int MinimumPasswordLength = 10;

    public Task RegisterAsync(string email, string username, string password, string? displayName, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/register",
            new { email, username, password, displayName }, ct);

    public Task ForgotPasswordAsync(string email, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/forgot-password", new { email }, ct);

    /// <summary>
    /// Completes the reset started by <see cref="ForgotPasswordAsync"/>, using the
    /// token from the emailed link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The new-password field is named <c>password</c>, <b>not</b> <c>newPassword</c>.
    /// That is live-verified rather than assumed: posting
    /// <c>{ token, newPassword }</c> returns 400 "Token and password are required",
    /// i.e. the server never saw a password at all. Don't "tidy" this to
    /// <c>newPassword</c>.
    /// </para>
    /// <para>
    /// Validation order, established by probing (2026-09-16): both-fields-present →
    /// password length → token validity. The three distinct 400 bodies are
    /// "Token and password are required", "Password must be at least 10 characters",
    /// and "Invalid or expired reset token". The server deliberately collapses
    /// *invalid* and *expired* into that one message, so a client genuinely cannot
    /// tell the two apart — don't claim otherwise in the UI.
    /// </para>
    /// <para>
    /// <b>Success path is unverified by design.</b> A successful reset against the
    /// shared test account in <c>.env</c> would lock out CI and every other client
    /// using those credentials, so only the failure paths above were exercised
    /// live. Per the repo's read-after-write rule the response body is not parsed;
    /// the caller signs in with the new password afterward, which is the real
    /// confirmation that the reset took.
    /// </para>
    /// </remarks>
    public Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/reset-password",
            new { token, password = newPassword }, ct);
}
