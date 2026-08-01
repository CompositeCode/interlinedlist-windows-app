using System.Security.Cryptography;
using System.Text;

namespace InterlinedList.Sync.Core;

/// <summary>SHA-256 (lowercase hex) of UTF-8 content — the change-detection primitive.</summary>
public static class ContentHash
{
    public static string Of(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexStringLower(bytes);
    }
}
