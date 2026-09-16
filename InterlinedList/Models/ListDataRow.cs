using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// One row of a list's data, from <c>GET /api/lists/{id}/data</c>.
/// </summary>
/// <remarks>
/// <para>
/// Field set reconciled against the live read payload 2026-09-16. Row keys are
/// exactly: <c>id, rowData, version, createdAt, updatedAt, createdByUser,
/// lastEditedByUser</c>.
/// </para>
/// <para>
/// <b><see cref="ListId"/> is deliberately NOT <c>required</c>.</b> The read
/// endpoint does not send it — declaring it required made
/// <c>System.Text.Json</c> throw
/// <c>"missing required properties including: 'listId'"</c>, so <i>any</i> list
/// holding at least one row failed to load. Watch for the asymmetry that hid
/// this: <c>POST /api/lists/{id}/data</c> <i>does</i> return <c>listId</c> in
/// its <c>{message, data:{…}}</c> envelope; only the read path omits it. The
/// caller already knows which list it asked for.
/// </para>
/// </remarks>
public sealed class ListDataRow
{
    public required string Id { get; init; }

    /// <summary>
    /// Owning list. Absent on the read path — see the remarks. Populated when a
    /// row comes back from a create/update response.
    /// </summary>
    public string? ListId { get; init; }

    public required Dictionary<string, JsonElement> RowData { get; init; }

    /// <summary>
    /// Monotonic row version, incremented on each edit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is NOT optimistic concurrency, despite looking like it.</b> An
    /// earlier revision of this comment claimed it was; probing disproved that
    /// (2026-09-16). Sending a deliberately stale <c>version</c> on
    /// <c>PUT /api/lists/{id}/data/{rowId}</c> is accepted:
    /// </para>
    /// <code>
    /// row at version 2, PUT with {"data":{…},"version":1}
    ///   -> 200, version becomes 3, the write lands
    /// </code>
    /// <para>
    /// So the server does not compare-and-swap on it — row writes are
    /// last-writer-wins and a concurrent edit is silently lost. Treat this as a
    /// display/audit value only. (Contrast the app-settings store, which DOES
    /// do real CAS via <c>baseVersion</c> and returns <c>409
    /// version_conflict</c> — see #41.)
    /// </para>
    /// </remarks>
    public int Version { get; init; }

    /// <summary>Server-assigned ordinal. Null on a schema-less list.</summary>
    public int? RowNumber { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Who added the row. Useful on a shared list — see the contributors work (#65).</summary>
    public ApiUser? CreatedByUser { get; init; }

    /// <summary>Who last edited it; null when never edited since creation.</summary>
    public ApiUser? LastEditedByUser { get; init; }

    /// <summary>
    /// Read-only "key: value, key2: value2" preview. Adequate while rows are
    /// freeform; the typed, schema-driven renderer is #21.
    /// </summary>
    /// <remarks>
    /// Note for anyone writing rows: <c>PUT /api/lists/{id}/data/{rowId}</c>
    /// <b>replaces</b> <c>rowData</c> rather than merging it. Verified live —
    /// a row holding <c>{a,b}</c> PUT with only <c>{a}</c> came back as
    /// <c>{a}</c>, silently dropping <c>b</c>. So an editor must re-send every
    /// key it knows about, echoing untouched values.
    /// </remarks>
    public string DisplaySummary =>
        string.Join(", ", RowData.Select(kv => $"{kv.Key}: {kv.Value}"));

    /// <summary>Edited since creation, per <see cref="LastEditedByUser"/>.</summary>
    public bool HasBeenEdited => LastEditedByUser is not null;
}
