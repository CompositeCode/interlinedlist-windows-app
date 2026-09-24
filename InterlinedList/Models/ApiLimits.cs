using System.IO;

namespace InterlinedList.Models;

/// <summary>
/// Server-published media and content limits from <c>GET /api/limits</c>.
/// </summary>
/// <remarks>
/// Live shape (verified 2026-09-15):
/// <code>
/// { "media": { "image": { "maxBytes": 1468006, "maxPixels": 1200,
///                         "acceptedFormats": ["jpeg","png","gif","webp"] },
///              "video": { "maxBytes": 3145728,
///                         "acceptedFormats": ["mp4","mov"] } },
///   "message": { "maxContentLength": 5000 } }
/// </code>
/// These are the server's caps. The limit that actually applies to a given
/// account can be lower — see <see cref="CurrentUser.MaxMessageLength"/>.
/// </remarks>
public sealed class ApiLimits
{
    public MediaLimits? Media { get; init; }
    public MessageLimits? Message { get; init; }
}

public sealed class MediaLimits
{
    public MediaTypeLimits? Image { get; init; }
    public MediaTypeLimits? Video { get; init; }
}

public sealed class MediaTypeLimits
{
    public long MaxBytes { get; init; }

    /// <summary>Longest-edge pixel cap. Only meaningful for images (0 when absent).</summary>
    public int MaxPixels { get; init; }

    public List<string>? AcceptedFormats { get; init; }

    /// <summary>
    /// The accepted formats as a WPF <c>OpenFileDialog.Filter</c> fragment,
    /// e.g. <c>"*.jpeg;*.png;*.gif;*.webp"</c>. Empty when the server sent none.
    /// </summary>
    public string FileDialogPattern =>
        AcceptedFormats is { Count: > 0 }
            ? string.Join(";", AcceptedFormats.Select(f => $"*.{f.TrimStart('.')}"))
            : string.Empty;

    /// <summary>True when the file's extension is one the server accepts.</summary>
    public bool AcceptsFile(string fileName)
    {
        if (AcceptedFormats is not { Count: > 0 }) return true;
        var ext = Path.GetExtension(fileName).TrimStart('.');
        if (ext.Length == 0) return false;
        // "jpeg" in the server list should also accept a .jpg file on disk.
        return AcceptedFormats.Any(f =>
            string.Equals(f.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(f, "jpeg", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ext, "jpg", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class MessageLimits
{
    public int MaxContentLength { get; init; }
}
