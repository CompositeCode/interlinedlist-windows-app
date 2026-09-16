namespace InterlinedList.Models;

/// <summary>
/// The three sharing roles the server accepts for every per-person grant,
/// share link and email invite on both lists and documents. The wire values
/// and their UI labels are fixed by the product (see /help/api/sharing and
/// /help/documents): sending anything else returns
/// <c>400 "Invalid role. Must be watcher, collaborator, or manager"</c>
/// (verified live 2026-09-16).
/// </summary>
public static class ShareRoles
{
    /// <summary>Read-only view of the resource. The server default when role is omitted.</summary>
    public const string Viewer = "watcher";

    /// <summary>View and edit content only — cannot rename, change visibility, or move.</summary>
    public const string Editor = "collaborator";

    /// <summary>Everything Editor can, plus rename, visibility, move and delete.</summary>
    public const string Admin = "manager";

    /// <summary>The label the web UI shows for a wire role value.</summary>
    public static string LabelFor(string? role) => role switch
    {
        Editor => "Edit",
        Admin => "Admin",
        Viewer or null or "" => "Read-only",
        // Forward-compatible: show an unrecognised server role verbatim rather
        // than mislabelling it as Read-only.
        _ => role,
    };

    /// <summary>One-line explanation of what a role can do, for the invite picker.</summary>
    public static string DescriptionFor(string? role) => role switch
    {
        Editor => "Can change content, but not rename, move, or make public",
        Admin => "Can also rename, move, change visibility, and delete",
        Viewer or null or "" => "Can view the content only",
        _ => string.Empty,
    };
}
