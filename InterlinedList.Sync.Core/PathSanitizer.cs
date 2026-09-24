using System.Text;

namespace InterlinedList.Sync.Core;

/// <summary>
/// Turns a document title or folder name into a safe path segment for NTFS/exFAT.
/// Rules mirror the reference sync agent: strip the reserved characters
/// <c>&lt; &gt; : " / \ | ? *</c> and control chars, trim trailing dots/spaces,
/// escape Windows reserved device names, fall back to "untitled", and cap length.
/// </summary>
public static class PathSanitizer
{
    private const int MaxSegmentLength = 120;

    private static readonly char[] Invalid =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Sanitize a single path segment (folder name or filename stem).</summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "untitled";

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            sb.Append(char.IsControl(ch) || Array.IndexOf(Invalid, ch) >= 0 ? '_' : ch);

        // NTFS forbids trailing dots and spaces on a segment.
        var name = sb.ToString().TrimEnd('.', ' ').Trim();

        if (name.Length == 0)
            return "untitled";

        if (name.Length > MaxSegmentLength)
            name = name[..MaxSegmentLength].TrimEnd('.', ' ');

        // Reserved device names (optionally with an extension) must be escaped.
        var stem = name;
        var dot = name.IndexOf('.');
        if (dot > 0) stem = name[..dot];
        if (ReservedNames.Contains(stem))
            name = "_" + name;

        return name;
    }

    /// <summary>Sanitize a title into a "<c>&lt;stem&gt;.md</c>" file name.</summary>
    public static string ToMarkdownFileName(string? title) => Sanitize(title) + ".md";
}
