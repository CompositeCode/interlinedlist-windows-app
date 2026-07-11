using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InterlinedList.Services;

/// <summary>
/// Persists the long-lived sync token to disk, DPAPI-encrypted for the current
/// Windows user. There is no server-side revoke endpoint for sync tokens, so
/// losing this file's protection would leak a standing credential — never
/// store the raw token unencrypted.
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
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void ClearToken()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
    }
}
