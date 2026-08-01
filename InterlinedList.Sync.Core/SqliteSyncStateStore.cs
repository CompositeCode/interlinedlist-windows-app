using System.Globalization;
using Microsoft.Data.Sqlite;

namespace InterlinedList.Sync.Core;

/// <summary>
/// SQLite-backed sync ledger (<c>state.db</c>). Holds the document↔file and
/// folder↔directory mappings plus the delta cursor. Single-connection, WAL mode;
/// one instance per sync process (the tray app), never shared cross-process.
/// </summary>
public sealed class SqliteSyncStateStore : ISyncStateStore
{
    private const string Iso = "O";
    private readonly SqliteConnection _db;

    public SqliteSyncStateStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Migrate();
    }

    /// <summary>In-memory store for unit tests (kept alive by the open connection).</summary>
    public static SqliteSyncStateStore InMemory() => new(":memory:");

    private void Migrate()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS documents (
                id                TEXT PRIMARY KEY,
                local_path        TEXT NOT NULL COLLATE NOCASE,
                folder_id         TEXT NULL,
                server_updated_at TEXT NOT NULL,
                content_hash      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_documents_local_path ON documents(local_path);

            CREATE TABLE IF NOT EXISTS folders (
                id            TEXT PRIMARY KEY,
                parent_id     TEXT NULL,
                name          TEXT NOT NULL,
                relative_path TEXT NOT NULL COLLATE NOCASE
            );
            CREATE INDEX IF NOT EXISTS ix_folders_relative_path ON folders(relative_path);

            CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
    }

    // ── Documents ───────────────────────────────────────────────────────────────

    public DocumentRecord? GetDocument(string id) =>
        QueryDocuments("WHERE id = $p", ("$p", id)).FirstOrDefault();

    public DocumentRecord? GetDocumentByPath(string localPath) =>
        QueryDocuments("WHERE local_path = $p", ("$p", localPath)).FirstOrDefault();

    public IReadOnlyList<DocumentRecord> AllDocuments() => QueryDocuments(null);

    public void UpsertDocument(DocumentRecord r)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (id, local_path, folder_id, server_updated_at, content_hash)
            VALUES ($id, $path, $folder, $updated, $hash)
            ON CONFLICT(id) DO UPDATE SET
                local_path = excluded.local_path,
                folder_id = excluded.folder_id,
                server_updated_at = excluded.server_updated_at,
                content_hash = excluded.content_hash;
            """;
        Bind(cmd, "$id", r.Id);
        Bind(cmd, "$path", r.LocalPath);
        Bind(cmd, "$folder", (object?)r.FolderId ?? DBNull.Value);
        Bind(cmd, "$updated", r.ServerUpdatedAt.ToString(Iso, CultureInfo.InvariantCulture));
        Bind(cmd, "$hash", r.ContentHash);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDocument(string id) => Exec("DELETE FROM documents WHERE id = $p", ("$p", id));

    private IReadOnlyList<DocumentRecord> QueryDocuments(string? where, params (string, object)[] ps)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, local_path, folder_id, server_updated_at, content_hash FROM documents "
                          + (where ?? string.Empty);
        foreach (var (n, v) in ps) Bind(cmd, n, v);
        var list = new List<DocumentRecord>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new DocumentRecord(
                rd.GetString(0),
                rd.GetString(1),
                rd.IsDBNull(2) ? null : rd.GetString(2),
                ParseDto(rd.GetString(3)),
                rd.GetString(4)));
        return list;
    }

    // ── Folders ─────────────────────────────────────────────────────────────────

    public FolderRecord? GetFolder(string id) =>
        QueryFolders("WHERE id = $p", ("$p", id)).FirstOrDefault();

    public FolderRecord? GetFolderByRelativePath(string relativePath) =>
        QueryFolders("WHERE relative_path = $p", ("$p", relativePath)).FirstOrDefault();

    public IReadOnlyList<FolderRecord> AllFolders() => QueryFolders(null);

    public void UpsertFolder(FolderRecord r)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO folders (id, parent_id, name, relative_path)
            VALUES ($id, $parent, $name, $rel)
            ON CONFLICT(id) DO UPDATE SET
                parent_id = excluded.parent_id,
                name = excluded.name,
                relative_path = excluded.relative_path;
            """;
        Bind(cmd, "$id", r.Id);
        Bind(cmd, "$parent", (object?)r.ParentId ?? DBNull.Value);
        Bind(cmd, "$name", r.Name);
        Bind(cmd, "$rel", r.RelativePath);
        cmd.ExecuteNonQuery();
    }

    public void DeleteFolder(string id) => Exec("DELETE FROM folders WHERE id = $p", ("$p", id));

    private IReadOnlyList<FolderRecord> QueryFolders(string? where, params (string, object)[] ps)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, parent_id, name, relative_path FROM folders " + (where ?? string.Empty);
        foreach (var (n, v) in ps) Bind(cmd, n, v);
        var list = new List<FolderRecord>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new FolderRecord(
                rd.GetString(0),
                rd.IsDBNull(1) ? null : rd.GetString(1),
                rd.GetString(2),
                rd.GetString(3)));
        return list;
    }

    // ── Cursor ──────────────────────────────────────────────────────────────────

    public DateTimeOffset? GetLastSyncAt()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM metadata WHERE key = 'last_sync_at'";
        return cmd.ExecuteScalar() is string s ? ParseDto(s) : null;
    }

    public void SetLastSyncAt(DateTimeOffset value) => Exec(
        "INSERT INTO metadata (key, value) VALUES ('last_sync_at', $v) " +
        "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
        ("$v", value.ToString(Iso, CultureInfo.InvariantCulture)));

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static DateTimeOffset ParseDto(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Bind(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private void Exec(string sql, params (string, object)[] ps)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) Bind(cmd, n, v);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
