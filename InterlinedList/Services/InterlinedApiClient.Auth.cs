using System.Net.Http;

namespace InterlinedList.Services;

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
/// Pre-login self-service (registration, password reset) and sign-out.
///
/// Register/forgot are public (no bearer). Request shapes verified against the
/// OpenAPI spec 2026-08-01: register { email, username, password, displayName },
/// forgot { email }. Responses aren't parsed — on success the caller either logs
/// in with the new credentials or tells the user to check their inbox.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task RegisterAsync(string email, string username, string password, string? displayName, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/register",
            new { email, username, password, displayName }, ct);

    public Task ForgotPasswordAsync(string email, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/forgot-password", new { email }, ct);

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
