using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InterlinedList.Services;

/// <summary>
/// Persists the long-lived sync token to disk, DPAPI-encrypted for the current
/// Windows user. Never store the raw token unencrypted — losing this file's
/// protection leaks a standing credential.
///
/// **This file is a cross-process contract, not just this app's cache.** The
/// InterlinedList.Sync tray utility reads the very same
/// <c>%LocalAppData%\InterlinedList\session.dat</c> (its <c>SessionTokenFile</c>
/// / <c>DpapiCredentialSource</c>) so that signing in from either place signs in
/// both, and its HTTP client re-reads it on *every* request — throwing
/// <c>NotSignedInException</c> the moment it reads back nothing. That makes
/// <see cref="ClearToken"/> the kill switch that stops the tray utility syncing.
///
/// Which matters more than it looks: <c>POST /api/auth/logout</c> does **not**
/// invalidate a bearer sync-token, and the server refuses to let a client revoke
/// its own token (both live-probed 2026-09-16 — see
/// <see cref="InterlinedApiClient.LogoutAsync"/> and
/// <see cref="InterlinedApiClient.TryRevokeCurrentSessionAsync"/>). Removing the
/// token locally is therefore the *only* thing sign-out can actually guarantee.
/// Treat <see cref="ClearToken"/> as security-critical.
/// </summary>
public static class CredentialStore
{
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InterlinedList", "session.dat");

    public static void SaveToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, protectedBytes);
    }

    public static string? LoadToken()
    {
        if (!File.Exists(TokenPath)) return null;
        try
        {
            var plainBytes = ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            // Anything undecryptable, truncated, locked or roamed-from-another-
            // machine reads as "no token" rather than propagating: this is called
            // from App.OnStartup, where a throw means the app dies with no window.
            // It is also half of the contract ClearToken relies on below.
            AppLog.Warn($"session.dat could not be read as a token ({ex.GetType().Name}); treating as signed out.");
            return null;
        }
    }

    /// <summary>
    /// Removes the stored token, and does it in the order that fails safe.
    ///
    /// The file is first overwritten with a single byte that cannot possibly
    /// DPAPI-decrypt, and only then deleted. Both readers of this file —
    /// <see cref="LoadToken"/> above and the tray utility's
    /// <c>SessionTokenFile.Read</c> — map undecryptable content to "no token", so
    /// the credential is dead the instant that write lands even if the delete
    /// afterwards fails (a read handle held by the tray process, an AV scanner, a
    /// read-only attribute). Deleting first and overwriting as a fallback would
    /// get this backwards: if the delete fails for a reason a write would also
    /// hit, a fully usable token stays on disk.
    ///
    /// Neither step throws. A sign-out that cannot finish tidying up must still
    /// be a sign-out, so problems are logged and swallowed.
    /// </summary>
    public static void ClearToken()
    {
        if (!File.Exists(TokenPath)) return;

        try
        {
            File.WriteAllBytes(TokenPath, [0]);
        }
        catch (Exception ex)
        {
            AppLog.Error("Sign-out: could not overwrite session.dat — it may still hold a usable token.", ex);
        }

        try
        {
            File.Delete(TokenPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Sign-out: could not delete session.dat ({ex.GetType().Name}: {ex.Message}); " +
                        "its contents were neutralized, so both readers will treat it as signed out.");
        }
    }
}
