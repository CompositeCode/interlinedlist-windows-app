using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The result of <c>POST /api/lists/{id}/refresh</c> — "Refresh from GitHub".
///
/// <para>
/// <b>Envelope deliberately loose.</b> The success body is <b>not</b> live
/// verified: the test account owns no GitHub-backed list, and creating one would
/// wire a real repository to it, so only the rejection path was exercised.
/// Probed live 2026-09-16 against a throwaway local list (created, probed,
/// deleted, confirmed gone):
/// </para>
/// <code>
/// POST /api/lists/{local-list-id}/refresh
///   → 400 { "error": "Refresh is only available for GitHub-backed lists",
///           "code": "bad_request" }
/// </code>
/// <para>
/// No mutation: re-reading the list afterwards returned an identical body with
/// an unchanged <c>updatedAt</c>. The OpenAPI spec documents <c>201</c> for
/// success with the body "not individually modelled yet", so rather than guess a
/// typed envelope this keeps <see cref="Raw"/> and picks up the handful of field
/// names the spec's neighbouring routes use, if any of them happen to be there.
/// Callers must re-read the list and its rows afterwards regardless — that is the
/// repo's read-after-write rule and here it is also the only way to know what
/// actually changed.
/// </para>
/// </summary>
public sealed class ListRefreshResult
{
    /// <summary>The whole response body, so nothing is lost to a wrong guess.</summary>
    public JsonElement Raw { get; init; }

    /// <summary><c>message</c>, if the server sends one.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// <c>refreshStatus</c>/<c>status</c>, if present. <c>POST /api/lists</c>
    /// documents a <c>refreshStatus</c> string "present only for GitHub-backed
    /// lists", so the refresh route plausibly echoes the same field.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Best-effort issue/row count, if the server reports one.</summary>
    public int? Imported { get; init; }

    /// <summary>What to show the user after a refresh; never empty.</summary>
    public string DisplayText =>
        (Message, Status, Imported) switch
        {
            ({ Length: > 0 } m, _, { } n) => $"{m} ({n} rows)",
            ({ Length: > 0 } m, _, _) => m,
            (_, { Length: > 0 } s, { } n) => $"Refreshed — {s} ({n} rows)",
            (_, { Length: > 0 } s, _) => $"Refreshed — {s}",
            (_, _, { } n) => $"Refreshed — {n} rows",
            _ => "Refreshed from GitHub."
        };

    /// <summary>Projects whatever the server sent, tolerating an empty body.</summary>
    public static ListRefreshResult FromJson(JsonElement body)
    {
        string? Str(params string[] names)
        {
            if (body.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in names)
            {
                if (body.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            }
            return null;
        }

        int? Int(params string[] names)
        {
            if (body.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in names)
            {
                if (body.TryGetProperty(name, out var p) &&
                    p.ValueKind == JsonValueKind.Number &&
                    p.TryGetInt32(out var value))
                {
                    return value;
                }
            }
            return null;
        }

        return new ListRefreshResult
        {
            Raw = body,
            Message = Str("message"),
            Status = Str("refreshStatus", "status"),
            Imported = Int("imported", "issuesImported", "rowCount", "count")
        };
    }
}
