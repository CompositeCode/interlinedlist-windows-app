using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Bulk revocation of standing sync-tokens.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: every row from <c>GET /api/user/sessions</c> is a
/// never-expiring bearer token, and the list reaches real scale — the test
/// account held <b>1,276</b> of them, 1,251 sharing the label <c>CLI</c>.
/// Revoking one at a time is not a usable control at that size.
/// </para>
/// <para>
/// There is no server-side bulk revoke, and the obvious candidate is a dead
/// end: <c>POST /api/auth/logout</c> returns <c>200 {"remaining":0}</c> even
/// with <b>no</b> <c>Authorization</c> header (<c>x-auth-type: "none"</c>) and
/// leaves the token working — <c>remaining</c> counts <i>cookie</i> sessions.
/// So the only real revoke is <c>DELETE /api/user/sessions/{id}</c>, and bulk
/// means looping it client-side.
/// </para>
/// <para>
/// <b>A session cannot revoke itself</b> — the current row returns
/// <c>400 {"error":"cannot_revoke_current_session"}</c>. Exactly one row
/// reports <see cref="ApiSession.IsCurrent"/>, and it is always skipped.
/// </para>
/// </remarks>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Revoke many sessions, reporting progress as it goes.
    /// </summary>
    /// <param name="sessions">
    /// The sessions to revoke. Any row with <see cref="ApiSession.IsCurrent"/>
    /// is skipped — it would fail, and revoking it would sign the caller out.
    /// </param>
    /// <param name="progress">
    /// Called after each attempt. With 1,200+ sequential deletes this runs for
    /// minutes, so a caller without progress would look frozen.
    /// </param>
    /// <remarks>
    /// A single failure must not abort the sweep: one already-revoked or
    /// otherwise unhappy row shouldn't strand the other thousand. Failures are
    /// counted and the first few messages kept for reporting.
    /// </remarks>
    public async Task<BulkRevokeResult> RevokeSessionsAsync(
        IEnumerable<ApiSession> sessions,
        IProgress<BulkRevokeProgress>? progress = null,
        CancellationToken ct = default)
    {
        var targets = sessions.Where(s => !s.IsCurrent).ToList();
        var revoked = 0;
        var failed = 0;
        var errors = new List<string>();

        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var session = targets[i];

            try
            {
                await RevokeSessionAsync(session.Id, ct);
                revoked++;
            }
            catch (InterlinedApiException ex)
            {
                failed++;
                // Keep a handful for the summary; a thousand identical messages
                // is noise, not diagnostics.
                if (errors.Count < 5)
                    errors.Add($"{session.DeviceLabelOrFallback}: {ex.Message}");
            }

            progress?.Report(new BulkRevokeProgress(i + 1, targets.Count, revoked, failed));
        }

        return new BulkRevokeResult(revoked, failed, errors, Skipped: sessions.Count(s => s.IsCurrent));
    }
}

/// <summary>How far a bulk revoke has got.</summary>
/// <param name="Completed">Attempts made.</param>
/// <param name="Total">Attempts planned.</param>
/// <param name="Revoked">Successes so far.</param>
/// <param name="Failed">Failures so far.</param>
public readonly record struct BulkRevokeProgress(int Completed, int Total, int Revoked, int Failed)
{
    public double Fraction => Total == 0 ? 1 : (double)Completed / Total;
}

/// <summary>Outcome of a bulk revoke.</summary>
/// <param name="Revoked">Sessions successfully revoked.</param>
/// <param name="Failed">Sessions that could not be revoked.</param>
/// <param name="Errors">Up to five failure messages, for reporting.</param>
/// <param name="Skipped">Rows deliberately not attempted (the current session).</param>
public sealed record BulkRevokeResult(int Revoked, int Failed, IReadOnlyList<string> Errors, int Skipped)
{
    public string Summary => Failed == 0
        ? $"Revoked {Revoked} session(s)." + (Skipped > 0 ? " This device was kept signed in." : "")
        : $"Revoked {Revoked}, failed {Failed}." + (Skipped > 0 ? " This device was kept signed in." : "");
}
