using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>What a completed sign-out actually achieved.</summary>
public enum LogoutOutcome
{
    /// <summary>The server revoked the sync-token — the credential is dead everywhere.</summary>
    TokenRevoked,

    /// <summary>
    /// Local state was cleared (so this device and the tray utility are signed
    /// out), but the sync-token itself is still valid server-side. This is the
    /// expected result today — see <see cref="InterlinedApiClient.LogoutAsync"/>.
    /// </summary>
    LocalOnly,
}

/// <summary>
/// Owns the logged-in state: restoring a saved session at launch, logging in,
/// and logging out. ViewModels bind to CurrentUser/IsAuthenticated directly.
/// </summary>
public sealed partial class SessionService : ObservableObject
{
    /// <summary>
    /// Ceiling on the whole server-side sign-out phase. Sign-out is a UI action
    /// the user expects to be instant, and the local teardown is what actually
    /// protects them, so the network side gets a short leash and no more.
    /// </summary>
    private static readonly TimeSpan ServerSignOutTimeout = TimeSpan.FromSeconds(8);

    private readonly InterlinedApiClient _api;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticated))]
    private CurrentUser? currentUser;

    public bool IsAuthenticated => CurrentUser is not null;

    public InterlinedApiClient Api => _api;

    public SessionService(InterlinedApiClient api)
    {
        _api = api;
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        var token = CredentialStore.LoadToken();
        if (token is null) return false;

        _api.AccessToken = token;
        try
        {
            CurrentUser = await _api.GetCurrentUserAsync(ct);
            return true;
        }
        catch (InterlinedApiException ex) when (ex.StatusCode is 401 or 403)
        {
            // A genuine rejection: the saved token is dead, so drop it. This is
            // the ONLY case that should clear it.
            AppLog.Info($"Saved session rejected ({ex.StatusCode}); clearing it and showing the login window.");
            ClearLocalSession();
            return false;
        }
        catch (Exception ex)
        {
            // Catch everything so nothing escapes into App.OnStartup — but do
            // NOT clear the token.
            //
            // A transient network/DNS/timeout/JSON failure must not cost the
            // user their saved credential. App.xaml.cs already has this exactly
            // right and says so: "The token is intentionally NOT cleared here;
            // only a genuine auth rejection inside TryRestoreSessionAsync
            // clears it." An earlier revision of this method cleared on ANY
            // exception, which meant launching on a flaky connection deleted
            // session.dat — and because the sync tray utility reads that same
            // file, it silently signed the tray out too. That is a worse
            // failure than the one it was guarding against.
            AppLog.Warn($"Session restore failed transiently; keeping the saved token. {ex.GetType().Name}: {ex.Message}");
            _api.AccessToken = null;
            CurrentUser = null;
            return false;
        }
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var token = await _api.RequestSyncTokenAsync(email, password, Environment.MachineName, ct);
        _api.AccessToken = token;
        CredentialStore.SaveToken(token);
        CurrentUser = await _api.GetCurrentUserAsync(ct);
    }

    /// <summary>
    /// Sign out: tell the server first, then tear down local state —
    /// unconditionally.
    ///
    /// The server phase is entirely best-effort and bounded by
    /// <see cref="ServerSignOutTimeout"/>. Every failure is swallowed and the
    /// local teardown happens in a <c>finally</c>, because a user who clicks
    /// "Sign out" on a flaky network must still end up signed out — being unable
    /// to reach the server is not a reason to leave a decrypted-on-demand
    /// standing credential sitting in <c>session.dat</c>.
    ///
    /// The returned outcome is the honest answer to "is that token dead?", which
    /// today is <see cref="LogoutOutcome.LocalOnly"/>: the server has no way for
    /// a client to invalidate its own sync-token
    /// (see <see cref="InterlinedApiClient.TryRevokeCurrentSessionAsync"/>).
    /// </summary>
    public async Task<LogoutOutcome> LogoutAsync(CancellationToken ct = default)
    {
        var outcome = LogoutOutcome.LocalOnly;
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(ServerSignOutTimeout);
            outcome = await SignOutServerSideAsync(bounded.Token);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sign-out: server phase failed; clearing local session anyway. {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ClearLocalSession();
            AppLog.Info($"Sign-out: local session cleared (outcome: {outcome}).");
        }

        return outcome;
    }

    /// <summary>
    /// Drops the in-memory user, the bearer token on the API client, and the
    /// on-disk <c>session.dat</c>. Exposed on its own for the one caller that has
    /// nothing left to tell the server — account deletion, where the account (and
    /// with it every token) is already gone.
    /// </summary>
    public void ClearLocalSession()
    {
        _api.AccessToken = null;
        CredentialStore.ClearToken();
        CurrentUser = null;
    }

    private async Task<LogoutOutcome> SignOutServerSideAsync(CancellationToken ct)
    {
        // Two independent best-effort steps: a failure in the first must not stop
        // the second, since only the second can actually revoke anything.
        try
        {
            await _api.LogoutAsync(ct);
            AppLog.Info("Sign-out: POST /api/auth/logout succeeded (clears any cookie session; provably a no-op for the bearer sync-token).");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sign-out: POST /api/auth/logout failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var revocation = await _api.TryRevokeCurrentSessionAsync(ct);
            switch (revocation)
            {
                case CurrentSessionRevocation.Revoked:
                    AppLog.Info("Sign-out: the sync-token was revoked server-side — it is now dead.");
                    return LogoutOutcome.TokenRevoked;

                case CurrentSessionRevocation.RefusedCurrentSession:
                    AppLog.Warn("Sign-out: the server refused to revoke the current session (cannot_revoke_current_session). " +
                                "The sync-token remains valid server-side; revoke it from Settings → API sessions on another signed-in device.");
                    return LogoutOutcome.LocalOnly;

                default:
                    AppLog.Warn($"Sign-out: could not revoke the sync-token ({revocation}). It may remain valid server-side.");
                    return LogoutOutcome.LocalOnly;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sign-out: revoking the current session failed: {ex.GetType().Name}: {ex.Message}");
            return LogoutOutcome.LocalOnly;
        }
    }
}
