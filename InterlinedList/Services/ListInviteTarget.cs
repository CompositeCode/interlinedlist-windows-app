using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Binds the shared invite panel to one list's /api/lists/{id}/invites routes.
/// </summary>
public sealed class ListInviteTarget : IInviteTarget
{
    private readonly InterlinedApiClient _api;
    private readonly string _listId;

    public ListInviteTarget(InterlinedApiClient api, string listId)
    {
        _api = api;
        _listId = listId;
    }

    public string ResourceNoun => "list";

    public Task<List<EmailInvite>> GetInvitesAsync(CancellationToken ct = default)
        => _api.GetListInvitesAsync(_listId, ct);

    public Task<EmailInvite> CreateInviteAsync(
        string email, string role, DateTimeOffset? expiresAt, CancellationToken ct = default)
        => _api.CreateListInviteAsync(_listId, email, role, expiresAt, ct);

    public Task RevokeInviteAsync(string token, CancellationToken ct = default)
        => _api.DeleteListInviteAsync(_listId, token, ct);

    public Task<string?> GetOwnerUserIdAsync(CancellationToken ct = default)
        => _api.GetListOwnerUserIdAsync(_listId, ct);

    public string InviteUrlFor(string token) => $"{ApiConfig.BaseUrl}lists/invite/{token}";
}
