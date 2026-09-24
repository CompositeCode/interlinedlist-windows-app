using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The kind of resource an <see cref="IInviteTarget"/> addresses. Lists and
/// documents use one identical sharing model, so one invite panel drives both.
/// </summary>
public enum InviteTargetKind
{
    List,
    Document,
}

/// <summary>
/// One resource's email-invite endpoints, seen from the UI. Lists and documents
/// have byte-identical invite payloads and differ only in the route segment and
/// the ownership envelope, so <c>InvitePanel</c> binds to this instead of
/// knowing about either domain.
/// </summary>
public interface IInviteTarget
{
    /// <summary>"list" or "document" — used in the panel's prose.</summary>
    string ResourceNoun { get; }

    Task<List<EmailInvite>> GetInvitesAsync(CancellationToken ct = default);

    Task<EmailInvite> CreateInviteAsync(
        string email, string role, DateTimeOffset? expiresAt, CancellationToken ct = default);

    Task RevokeInviteAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// The resource owner's user id, or null when the server didn't report one.
    /// Only the true owner may manage sharing, so the panel gates on this.
    /// </summary>
    Task<string?> GetOwnerUserIdAsync(CancellationToken ct = default);

    /// <summary>
    /// The invite landing address for a token. The create response returns this
    /// as <c>url</c>; the GET listing doesn't, so it's rebuilt from the token
    /// (format verified live 2026-09-16) to offer Copy link on every row.
    /// </summary>
    string InviteUrlFor(string token);
}

/// <summary>
/// Builds the <see cref="IInviteTarget"/> for a selected resource so the view
/// layer stays free of per-domain wiring.
/// </summary>
public static class InviteTargets
{
    public static IInviteTarget? For(InviteTargetKind kind, string? resourceId, InterlinedApiClient api)
    {
        if (string.IsNullOrEmpty(resourceId)) return null;

        return kind switch
        {
            InviteTargetKind.List => new ListInviteTarget(api, resourceId),
            // Document invites land with issue #56 (same endpoints under
            // /api/documents) and slot in here.
            _ => null,
        };
    }
}
