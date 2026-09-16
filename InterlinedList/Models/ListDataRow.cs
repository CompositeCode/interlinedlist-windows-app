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
    /// Monotonic row version. Incremented per edit — the hook for optimistic
    /// concurrency on row updates, so two clients editing one row can be
    /// detected rather than silently last-writer-wins.
    /// </summary>
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
    public string DisplaySummary =>
        string.Join(", ", RowData.Select(kv => $"{kv.Key}: {kv.Value}"));

    /// <summary>Edited since creation, per <see cref="LastEditedByUser"/>.</summary>
    public bool HasBeenEdited => LastEditedByUser is not null;
}
