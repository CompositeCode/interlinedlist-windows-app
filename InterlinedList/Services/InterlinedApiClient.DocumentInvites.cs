using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Email invites to a document — the same sharing model as
/// InterlinedApiClient.ListInvites.cs, and the path the web Share window leads
/// with. Distinct from the share links and collaborators in
/// InterlinedApiClient.Documents.cs: a share link is a bearer capability and a
/// collaborator is an existing account, whereas an invite is bound to an email
/// address that may not have an account yet.
///
/// All three routes are owner-only (a non-owner gets 404 — existence is never
/// leaked). Creating is subscriber-gated (403 for a free owner); listing and
/// revoking are not, so an owner whose subscription lapsed can always shut off
/// access they previously granted.
///
/// Verified live 2026-09-16 against a throwaway document, and the payloads are
/// byte-identical to the list ones — hence the shared <see cref="EmailInvite"/>
/// wire type:
/// <code>
/// GET    /api/documents/{id}/invites            → 200 { "invites": [ … ] }
/// POST   /api/documents/{id}/invites            → 201 { email, role, expiresAt, url }
/// DELETE /api/documents/{id}/invites/{token}    → 200 { "revoked": true }
/// </code>
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<List<EmailInvite>> GetDocumentInvitesAsync(string documentId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/documents/{documentId}/invites", ct);
        return json.TryGetProperty("invites", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<EmailInvite>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// Invite an email address to this document. Re-inviting the same address is
    /// idempotent server-side: it re-issues a fresh token and resets the invite
    /// to unclaimed. An invite email carrying the returned url is sent to the
    /// address best-effort (fire-and-forget), so this is a real outbound email —
    /// callers should not exercise it against addresses they don't own.
    ///
    /// The returned envelope IS live-verified, but it carries no token, so
    /// callers still re-read <see cref="GetDocumentInvitesAsync"/> afterwards to
    /// get the token they need for <see cref="DeleteDocumentInviteAsync"/>.
    /// </summary>
    public Task<EmailInvite> CreateDocumentInviteAsync(
        string documentId,
        string email,
        string role = ShareRoles.Viewer,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
        => SendJsonAsync<EmailInvite>(HttpMethod.Post, $"api/documents/{documentId}/invites",
            new { email, role, expiresAt = expiresAt?.UtcDateTime }, ct);

    public Task DeleteDocumentInviteAsync(string documentId, string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete,
            $"api/documents/{documentId}/invites/{Uri.EscapeDataString(token)}", null, ct);

    /// <summary>
    /// The document owner's user id, read straight off the GET
    /// /api/documents/{id} "document" envelope (verified live 2026-09-16 — the
    /// payload carries userId, which <see cref="DocumentSummary"/> does not
    /// model). Only the true owner can manage sharing — not even a manager-role
    /// collaborator can — so the share UI compares this against the signed-in
    /// user rather than assuming the open document is owned. Returns null when
    /// the field is missing.
    ///
    /// Note the envelope key differs from the list equivalent: documents nest
    /// under "document", lists under "data".
    /// </summary>
    public async Task<string?> GetDocumentOwnerUserIdAsync(string documentId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/documents/{documentId}", ct);
        return json.TryGetProperty("document", out var doc)
               && doc.TryGetProperty("userId", out var userId)
               && userId.ValueKind == JsonValueKind.String
            ? userId.GetString()
            : null;
    }
}
