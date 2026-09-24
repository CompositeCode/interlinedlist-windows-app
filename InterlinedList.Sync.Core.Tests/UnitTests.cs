using InterlinedList.Sync.Core;
using Xunit;

namespace InterlinedList.Sync.Core.Tests;

public class PathSanitizerTests
{
    [Theory]
    [InlineData("a/b:c*d", "a_b_c_d")]
    [InlineData("clean-name", "clean-name")]
    [InlineData("trailing.  ", "trailing")]
    [InlineData("   ", "untitled")]
    [InlineData("...", "untitled")]
    public void Sanitize_scrubs_invalid_segments(string input, string expected) =>
        Assert.Equal(expected, PathSanitizer.Sanitize(input));

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Sanitize_escapes_reserved_device_names(string reserved) =>
        Assert.Equal("_" + reserved, PathSanitizer.Sanitize(reserved));

    [Fact]
    public void Sanitize_caps_length() =>
        Assert.True(PathSanitizer.Sanitize(new string('a', 500)).Length <= 120);

    [Fact]
    public void ToMarkdownFileName_appends_extension() =>
        Assert.Equal("My Note.md", PathSanitizer.ToMarkdownFileName("My Note"));
}

public class ConflictResolverTests
{
    [Theory]
    [InlineData(false, false, SyncAction.NoOp)]
    [InlineData(true, false, SyncAction.Push)]
    [InlineData(false, true, SyncAction.Pull)]
    [InlineData(true, true, SyncAction.ConflictCopyThenPull)]
    public void Decide_matches_truth_table(bool local, bool remote, SyncAction expected) =>
        Assert.Equal(expected, ConflictResolver.Decide(local, remote));
}

public class FileMapperTests
{
    private readonly FileMapper _m = new(Path.Combine(Path.GetTempPath(), "il-map-root"));

    [Fact]
    public void DocumentPath_at_root()
    {
        var expected = Path.Combine(_m.Root, "Note.md");
        Assert.Equal(expected, _m.DocumentPath(null, "Note"));
    }

    [Fact]
    public void DocumentPath_in_subfolder()
    {
        var expected = Path.Combine(_m.Root, "Projects", "Note.md");
        Assert.Equal(expected, _m.DocumentPath("Projects", "Note"));
    }

    [Fact]
    public void RelativeDirectoryOf_returns_empty_at_root() =>
        Assert.Equal(string.Empty, _m.RelativeDirectoryOf(Path.Combine(_m.Root, "Note.md")));

    [Fact]
    public void RelativeDirectoryOf_returns_subpath()
    {
        var rel = _m.RelativeDirectoryOf(Path.Combine(_m.Root, "A", "B", "Note.md"));
        Assert.Equal(Path.Combine("A", "B"), rel);
    }

    [Fact]
    public void ConflictPath_is_recognized_by_IsConflictFile()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var conflict = _m.ConflictPath(Path.Combine(_m.Root, "Note.md"), now);
        Assert.EndsWith(".conflict-20260801T120000.md", conflict);
        Assert.True(_m.IsConflictFile(conflict));
        Assert.False(_m.IsConflictFile(Path.Combine(_m.Root, "Note.md")));
    }
}

public class SqliteSyncStateStoreTests
{
    [Fact]
    public void Documents_roundtrip()
    {
        using var store = SqliteSyncStateStore.InMemory();
        var rec = new DocumentRecord("d1", "/vault/a.md", "f1", DateTimeOffset.UnixEpoch, "hash1");
        store.UpsertDocument(rec);

        Assert.Equal(rec, store.GetDocument("d1"));
        Assert.Equal(rec, store.GetDocumentByPath("/vault/a.md"));
        Assert.Single(store.AllDocuments());

        store.UpsertDocument(rec with { ContentHash = "hash2" });
        Assert.Equal("hash2", store.GetDocument("d1")!.ContentHash);

        store.DeleteDocument("d1");
        Assert.Null(store.GetDocument("d1"));
    }

    [Fact]
    public void Folders_roundtrip()
    {
        using var store = SqliteSyncStateStore.InMemory();
        var f = new FolderRecord("f1", null, "Projects", "Projects");
        store.UpsertFolder(f);
        Assert.Equal(f, store.GetFolder("f1"));
        Assert.Equal(f, store.GetFolderByRelativePath("Projects"));
        store.DeleteFolder("f1");
        Assert.Empty(store.AllFolders());
    }

    [Fact]
    public void Cursor_roundtrip()
    {
        using var store = SqliteSyncStateStore.InMemory();
        Assert.Null(store.GetLastSyncAt());
        var t = new DateTimeOffset(2026, 8, 1, 10, 30, 0, TimeSpan.Zero);
        store.SetLastSyncAt(t);
        Assert.Equal(t, store.GetLastSyncAt());
    }
}
