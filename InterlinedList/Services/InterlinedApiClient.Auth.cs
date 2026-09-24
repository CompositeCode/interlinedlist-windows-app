using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The email/verification standing of an account, read straight off
/// <c>GET /api/user</c>.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> added to <see cref="CurrentUser"/>: that model
/// already carries typed <c>PendingEmail</c> and <c>AccountStatus</c>
/// properties, and duplicating them here would collide. This record reads the
/// fields out of the raw response element instead, so the two changes stayed
/// independent while both were in flight.
/// <see cref="InterlinedApiClient.GetUserAndEmailStandingAsync"/> can now be
/// collapsed into a plain <c>GetCurrentUserAsync()</c> and this record deleted —
/// a small follow-up, not done here to keep this merge reviewable.
/// </remarks>
/// <param name="Email">The address currently in force.</param>
/// <param name="EmailVerified">Whether <paramref name="Email"/> has been verified.</param>
/// <param name="PendingEmail">
/// A requested new address awaiting confirmation, or null when no change is in
/// flight. This is what drives the confirm/undo affordances.
/// </param>
/// <param name="AccountStatus"><c>new</c>, <c>active</c>, <c>restricted</c>, <c>suspended</c>, <c>banned</c>.</param>
public sealed record EmailStanding(string? Email, bool EmailVerified, string? PendingEmail, string? AccountStatus);

/// <summary>
/// What a sign-out managed to do to the standing bearer sync-token server-side.
/// </summary>
public enum CurrentSessionRevocation
{
    /// <summary>The server accepted the revoke — the sync-token is dead.</summary>
    Revoked,

    /// <summary>
    /// The server refused to revoke the session we authenticated with
    /// (<c>400 cannot_revoke_current_session</c>). This is what happens today.
    /// </summary>
    RefusedCurrentSession,

    /// <summary>No row came back marked <c>isCurrent</c>, so we couldn't identify our own token.</summary>
    SessionNotFound,

    /// <summary>The attempt failed for another reason (network, 5xx, unexpected status).</summary>
    Failed,
}

/// <summary>
/// Everything on <c>/api/auth/*</c> this client uses: token-driven self-service
/// (registration, password reset, email verification and email-change
/// confirmation) and <b>sign-out</b>.
/// </summary>
/// <remarks>
/// <para>
/// The self-service calls are all <b>public</b> (OpenAPI <c>security: []</c>) —
/// no bearer token is needed, and sending one changes nothing, confirmed live
/// 2026-09-16 by issuing each twice, with and without a valid sync token, and
/// getting byte-identical responses. Being public is the load-bearing property:
/// an emailed token is sufficient on its own, so all of it works from the login
/// window before a session exists.
/// </para>
/// <para>
/// Request shapes verified against the OpenAPI spec and
/// <c>/help/api/authentication</c>: register <c>{email, username, password,
/// displayName}</c>, forgot <c>{email}</c>, reset <c>{token, password}</c>, and
/// <c>{token}</c> for each of the three verify/undo calls. Responses aren't
/// parsed — on success the caller either signs in with the new credentials or
/// re-reads <c>GET /api/user</c>.
/// </para>
/// <para>
/// One exception: <c>POST /api/auth/send-verification-email</c> (resend) is
/// <c>cookieAuth</c>-only and is therefore <b>not</b> wrapped here — see
/// <see cref="OpenWebVerificationSettings"/>.
/// </para>
/// <para>
/// Sign-out has nothing in common with the above beyond the URL prefix, and is
/// counter-intuitive enough to have its own section comment below. Read it
/// before changing that flow.
/// </para>
/// </remarks>
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

    // ── Email verification & email-change confirmation ──────────────────────────
    // All three take { token } and, like reset-password, are genuinely public —
    // each was probed twice live (2026-09-16), with and without a valid bearer
    // sync token, and answered identically. That matters: it means the emailed
    // token alone is sufficient, so verification works from the login window
    // before any session exists. Response bodies are not parsed (read-after-write);
    // callers re-read GET /api/user instead, which is the only trustworthy
    // confirmation that emailVerified/accountStatus actually moved.

    /// <summary>
    /// Completes email verification with the token from the emailed link.
    /// Failure envelope (live): 400 "Verification token is required" when the
    /// field is absent, 400 "Invalid or expired verification token" otherwise.
    /// </summary>
    public Task VerifyEmailAsync(string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/verify-email", new { token }, ct);

    /// <summary>
    /// Confirms a new address requested via <see cref="RequestEmailChangeAsync"/>,
    /// using the token mailed to that new address. Same failure envelope as
    /// <see cref="VerifyEmailAsync"/>; the spec also lists 409, presumably for a
    /// change that has already been settled.
    /// </summary>
    public Task VerifyEmailChangeAsync(string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/verify-email-change", new { token }, ct);

    /// <summary>
    /// Reverses an email change, using the undo token mailed to the <i>old</i>
    /// address — the safety valve for a change the account owner didn't make.
    /// Failure envelope (live): 400 "Undo token is required" / "Invalid or expired
    /// undo link". Note the wording differs from the verify endpoints, so don't
    /// match on the verify strings here.
    /// </summary>
    public Task UndoEmailChangeAsync(string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/undo-email-change", new { token }, ct);

    /// <summary>
    /// Re-reads <c>GET /api/user</c> once and returns it both ways: the typed
    /// <see cref="CurrentUser"/> to refresh the session with, and the
    /// <see cref="EmailStanding"/> for the fields the model doesn't carry yet.
    /// Requires a bearer token (unlike the three verify calls above).
    /// </summary>
    /// <remarks>
    /// Deliberately one request serving two shapes. <c>pendingEmail</c> and
    /// <c>accountStatus</c> aren't on <see cref="CurrentUser"/> until PR #131
    /// lands, so reading the raw element is the only way to get them without
    /// editing that PR's file — but calling <c>GET /api/user</c> twice to collect
    /// both halves would be wasteful, and the two reads could disagree.
    /// </remarks>
    public async Task<(CurrentUser? User, EmailStanding Standing)> GetUserAndEmailStandingAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user", ct);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("user", out var user))
            return (null, new EmailStanding(null, false, null, null));

        var standing = new EmailStanding(
            Text(user, "email"),
            user.TryGetProperty("emailVerified", out var verified) && verified.ValueKind == JsonValueKind.True,
            Text(user, "pendingEmail"),
            Text(user, "accountStatus"));

        return (user.Deserialize<CurrentUser>(JsonOptions), standing);

        static string? Text(JsonElement owner, string name)
            => owner.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
    }

    /// <summary>
    /// Hands the browser the web settings page so the user can resend their
    /// verification email.
    /// </summary>
    /// <remarks>
    /// <b>This is a handoff, not a wrapper, on purpose.</b>
    /// <c>POST /api/auth/send-verification-email</c> declares <c>cookieAuth</c> in
    /// the spec, and that is real, not stale documentation: probed live
    /// 2026-09-16 it answers <c>401 {"error":"Unauthorized"}</c> with a
    /// <i>valid</i> bearer sync token, exactly as it does with a bogus one and
    /// with no credentials at all. A native bearer-token client structurally
    /// cannot obtain the session cookie, so an in-app "Resend" button could only
    /// ever fail. Same pattern as OAuth linking and "Manage account on the web".
    /// The URL 307-redirects to /login when the browser has no session, which is
    /// the correct place to land anyway.
    /// </remarks>
    public void OpenWebVerificationSettings()
    {
        // UseShellExecute = true is required — without it, Process.Start on
        // .NET Core/5+ won't hand the URL to the OS's default browser.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = $"{ApiConfig.BaseUrl}settings",
            UseShellExecute = true
        });
    }

    // ── Sign-out ────────────────────────────────────────────────────────────────
    // Read the comments here before "fixing" the sign-out flow. Both calls below
    // were live-probed against the test account on 2026-09-16 with throwaway
    // tokens, and the conclusion is counter-intuitive: POST /api/auth/logout
    // does NOT invalidate a bearer sync-token, and the endpoint that does
    // refuses to be pointed at the caller's own token.

    /// <summary>
    /// <c>POST /api/auth/logout</c>. Terminates the *cookie* session.
    ///
    /// Live-probed 2026-09-16: returns <c>200</c> with
    /// <c>{"message":"Logged out successfully","remaining":0}</c> — identically
    /// with no body, with <c>{}</c>, with <c>{"all":"true"}</c>, with
    /// <c>?all=true</c>, and even with no <c>Authorization</c> header at all. The
    /// OpenAPI spec agrees it never reads the bearer token: it is declared
    /// <c>x-auth-type: "none"</c> with <c>security: []</c>. <c>remaining</c> is
    /// the number of cookie sessions left standing, which is always 0 for a
    /// native bearer client because it has none.
    ///
    /// **It does not invalidate the sync-token.** After five successive logout
    /// calls the same token still returned <c>200</c> from <c>GET /api/user</c>,
    /// <c>GET /api/messages</c> and <c>GET /api/user/sessions</c>, and its row
    /// was still listed by <c>GET /api/user/sessions</c> with a freshly bumped
    /// <c>lastUsedAt</c>. Use <see cref="TryRevokeCurrentSessionAsync"/> for the
    /// mechanism that actually kills a token.
    ///
    /// Still called on sign-out: it is the documented sign-out endpoint, it is
    /// cheap, and it does clear any cookie session this process picked up via the
    /// OAuth browser-handoff flows. The response body isn't parsed
    /// (read-after-write discipline — nothing here is trusted).
    /// </summary>
    public Task LogoutAsync(CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/logout", new { }, ct);

    /// <summary>
    /// Best-effort attempt to revoke the sync-token this client is authenticating
    /// with, via the one endpoint that genuinely works:
    /// <c>DELETE /api/user/sessions/{id}</c>.
    ///
    /// That endpoint really does destroy a token — live-verified 2026-09-16
    /// against a throwaway token: <c>204</c>, after which the revoked token
    /// returned <c>401 {"error":"Unauthorized","code":"unauthorized"}</c> and its
    /// row was gone from <c>GET /api/user/sessions</c>.
    ///
    /// It just won't do it for *us*: the row we authenticate with comes back
    /// flagged <c>isCurrent: true</c> (exactly one row is, verified), and
    /// DELETEing that id returns
    /// <c>400 {"error":"cannot_revoke_current_session","code":"bad_request"}</c>.
    /// So a client structurally cannot invalidate its own sync-token today — the
    /// expected result of this method is
    /// <see cref="CurrentSessionRevocation.RefusedCurrentSession"/>.
    ///
    /// It is wired up anyway for two reasons: sign-out can then report honestly
    /// whether the credential is dead, and the day the server lifts that
    /// restriction sign-out becomes genuinely secure with no code change. It
    /// returns an outcome rather than throwing, because sign-out must never be
    /// blocked by the server's answer.
    ///
    /// Note the cost: <c>GET /api/user/sessions</c> is unpaginated and returned
    /// ~189 KB / 1,284 rows / 0.86 s on the test account, so only call this on an
    /// explicit, user-initiated sign-out — never on a hot path.
    /// </summary>
    public async Task<CurrentSessionRevocation> TryRevokeCurrentSessionAsync(CancellationToken ct = default)
    {
        var sessions = await GetSessionsAsync(ct);
        if (sessions.FirstOrDefault(s => s.IsCurrent)?.Id is not { Length: > 0 } id)
            return CurrentSessionRevocation.SessionNotFound;

        try
        {
            await RevokeSessionAsync(id, ct);
            return CurrentSessionRevocation.Revoked;
        }
        catch (InterlinedApiException ex) when (ex.StatusCode == 400)
        {
            // cannot_revoke_current_session — the documented refusal, not a fault.
            return CurrentSessionRevocation.RefusedCurrentSession;
        }
    }
}
