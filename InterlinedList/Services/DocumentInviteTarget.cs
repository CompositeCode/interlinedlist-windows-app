using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Binds the shared invite panel to one document's
/// /api/documents/{id}/invites routes. Mirror image of
/// <see cref="ListInviteTarget"/> — the sharing model is identical, so the panel
/// itself needs no document-specific branch.
/// </summary>
public sealed class DocumentInviteTarget : IInviteTarget
{
    private readonly InterlinedApiClient _api;
    private readonly string _documentId;

    public DocumentInviteTarget(InterlinedApiClient api, string documentId)
    {
        _api = api;
        _documentId = documentId;
    }

    public string ResourceNoun => "document";

    public Task<List<EmailInvite>> GetInvitesAsync(CancellationToken ct = default)
        => _api.GetDocumentInvitesAsync(_documentId, ct);

    public Task<EmailInvite> CreateInviteAsync(
        string email, string role, DateTimeOffset? expiresAt, CancellationToken ct = default)
        => _api.CreateDocumentInviteAsync(_documentId, email, role, expiresAt, ct);

    public Task RevokeInviteAsync(string token, CancellationToken ct = default)
        => _api.DeleteDocumentInviteAsync(_documentId, token, ct);

    public Task<string?> GetOwnerUserIdAsync(CancellationToken ct = default)
        => _api.GetDocumentOwnerUserIdAsync(_documentId, ct);

    public string InviteUrlFor(string token) => $"{ApiConfig.BaseUrl}documents/invite/{token}";
}
