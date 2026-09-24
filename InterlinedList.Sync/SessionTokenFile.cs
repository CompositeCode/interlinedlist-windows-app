using System.Security.Cryptography;
using System.Text;

namespace InterlinedList.Sync;

/// <summary>
/// Reads/writes the SAME DPAPI-encrypted <c>session.dat</c> the main app uses
/// (<c>ProtectedData</c>, CurrentUser scope), so signing in from either the app or
/// this utility works for both. Never throws — a bad/absent file reads as null.
/// </summary>
internal static class SessionTokenFile
{
    public static string? Read()
    {
        try
        {
            if (!File.Exists(SyncPaths.SessionFile)) return null;
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(SyncPaths.SessionFile), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SyncPaths.SessionFile)!);
        var prot = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SyncPaths.SessionFile, prot);
    }

    public static void Clear()
    {
        try { if (File.Exists(SyncPaths.SessionFile)) File.Delete(SyncPaths.SessionFile); }
        catch { /* best effort */ }
    }
}

/// <summary>Supplies the shared bearer token to the engine's API client.</summary>
internal sealed class DpapiCredentialSource : Core.ICredentialSource
{
    public string? GetToken() => SessionTokenFile.Read();
}
