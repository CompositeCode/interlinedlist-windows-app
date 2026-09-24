using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Email invites to a list — the "invite people, keep it private" path the web
/// Share window leads with. Distinct from the share links and watchers in
/// InterlinedApiClient.Lists.cs: a share link is a bearer capability and a
/// watcher is an existing account, whereas an invite is bound to an email
/// address that may not have an account yet.
///
/// All three routes are owner-only (a non-owner gets 404 — existence is never
/// leaked). Creating is subscriber-gated (403 for a free owner); listing and
/// revoking are not, so an owner whose subscription lapsed can always shut off
/// access they previously granted.
///
/// Verified live 2026-09-16 against a throwaway list:
/// <code>
/// GET    /api/lists/{id}/invites                → 200 { "invites": [ … ] }
/// POST   /api/lists/{id}/invites                → 201 { email, role, expiresAt, url }
/// DELETE /api/lists/{id}/invites/{token}        → 200 { "revoked": true }
/// DELETE …/invites/{unknown token}              → 404 { error: "Invite not found or access denied" }
/// POST   … { email: "not-an-email" }            → 400 { error: "A valid email address is required" }
/// POST   … { role: "bogus" }                    → 400 { error: "Invalid role. Must be watcher, collaborator, or manager" }
/// </code>
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<List<EmailInvite>> GetListInvitesAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/invites", ct);
        return json.TryGetProperty("invites", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<EmailInvite>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// Invite an email address to this list. Re-inviting the same address is
    /// idempotent server-side: it re-issues a fresh token and resets the invite
    /// to unclaimed. An invite email carrying the returned url is sent to the
    /// address best-effort (fire-and-forget), so this is a real outbound email —
    /// callers should not exercise it against addresses they don't own.
    ///
    /// The returned envelope IS live-verified, but it carries no token, so
    /// callers still re-read <see cref="GetListInvitesAsync"/> afterwards to get
    /// the token they need for <see cref="DeleteListInviteAsync"/>.
    /// </summary>
    public Task<EmailInvite> CreateListInviteAsync(
        string listId,
        string email,
        string role = ShareRoles.Viewer,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
        => SendJsonAsync<EmailInvite>(HttpMethod.Post, $"api/lists/{listId}/invites",
            new { email, role, expiresAt = expiresAt?.UtcDateTime }, ct);

    public Task DeleteListInviteAsync(string listId, string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete,
            $"api/lists/{listId}/invites/{Uri.EscapeDataString(token)}", null, ct);

    /// <summary>
    /// The list owner's user id, read straight off the GET /api/lists/{id}
    /// "data" envelope (verified live 2026-09-16 — the payload carries userId,
    /// which <see cref="ListSummary"/> does not model). Only the true owner can
    /// manage sharing — not even a manager-role collaborator can — so the share
    /// UI compares this against the signed-in user rather than assuming the
    /// selected list is owned. Returns null when the field is missing.
    /// </summary>
    public async Task<string?> GetListOwnerUserIdAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}", ct);
        return json.TryGetProperty("data", out var data)
               && data.TryGetProperty("userId", out var userId)
               && userId.ValueKind == JsonValueKind.String
            ? userId.GetString()
            : null;
    }
}
