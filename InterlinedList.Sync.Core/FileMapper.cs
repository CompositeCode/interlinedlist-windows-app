using System.Text.RegularExpressions;

namespace InterlinedList.Sync.Core;

/// <summary>
/// Maps between server documents/folders and local file paths under the sync root.
/// Folders are materialized as real subdirectories (the folder-tree mirroring that
/// this client adds on top of the reference agent's flat layout).
/// </summary>
public sealed partial class FileMapper(string root)
{
    /// <summary>Absolute sync-folder root (the user's chosen vault directory).</summary>
    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>Absolute path of a document given its folder's relative path and title.</summary>
    public string DocumentPath(string? folderRelativePath, string title)
    {
        var fileName = PathSanitizer.ToMarkdownFileName(title);
        return string.IsNullOrEmpty(folderRelativePath)
            ? Path.Combine(Root, fileName)
            : Path.Combine(Root, folderRelativePath, fileName);
    }

    /// <summary>Absolute path of a folder given its relative path.</summary>
    public string FolderPath(string relativePath) =>
        string.IsNullOrEmpty(relativePath) ? Root : Path.Combine(Root, relativePath);

    /// <summary>Relative directory of an absolute file path (""/root when at the top).</summary>
    public string RelativeDirectoryOf(string absolutePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(absolutePath)) ?? Root;
        var rel = Path.GetRelativePath(Root, dir);
        return rel == "." ? string.Empty : rel;
    }

    /// <summary>Conflict-copy path: <c>&lt;stem&gt;.conflict-&lt;yyyyMMddTHHmmss&gt;.md</c> beside the original.</summary>
    public string ConflictPath(string documentAbsolutePath, DateTimeOffset now)
    {
        var dir = Path.GetDirectoryName(documentAbsolutePath) ?? Root;
        var stem = Path.GetFileNameWithoutExtension(documentAbsolutePath);
        var stamp = now.UtcDateTime.ToString("yyyyMMddTHHmmss");
        return Path.Combine(dir, $"{stem}.conflict-{stamp}.md");
    }

    /// <summary>True for a conflict copy — these are excluded from the local-change scan.</summary>
    public bool IsConflictFile(string path) => ConflictRegex().IsMatch(Path.GetFileName(path));

    [GeneratedRegex(@"\.conflict-\d{8}T\d{6}\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex ConflictRegex();
}
