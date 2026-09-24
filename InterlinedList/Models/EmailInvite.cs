namespace InterlinedList.Models;

/// <summary>
/// An email invite to a private list or document. Unlike a <see cref="ShareLink"/>
/// (a bearer capability — whoever holds the token has access) an invite is bound
/// to the invited address: the token only becomes access once a signed-in user
/// with that verified email claims it, so a forwarded invite link is useless to
/// anyone else. The invited person may not have an account yet.
///
/// One wire shape serves lists and documents. Verified live 2026-09-16:
/// <code>
/// GET  /api/{lists|documents}/{id}/invites
///      → { "invites": [ { email, role, expiresAt, accepted, createdAt, token } ] }
/// POST /api/{lists|documents}/{id}/invites  { email, role, expiresAt }
///      → 201 { email, role, expiresAt, url }
/// </code>
/// Note the asymmetry: <see cref="Token"/> comes back only from the GET and
/// <see cref="Url"/> only from the POST, so both are nullable. Revoking needs
/// the token, which is why callers refresh from the GET after creating one.
/// </summary>
public sealed class EmailInvite
{
    public required string Email { get; init; }

    /// <summary>A <see cref="ShareRoles"/> wire value; absent means Read-only.</summary>
    public string? Role { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Flips to true once a signed-in user with this email has claimed it.</summary>
    public bool Accepted { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The revoke key. Present on the GET listing, absent from the POST response.</summary>
    public string? Token { get; init; }

    /// <summary>The invite landing address. Present on the POST response, absent from the GET listing.</summary>
    public string? Url { get; init; }

    public string RoleLabel => ShareRoles.LabelFor(Role);

    public string StatusLabel => Accepted ? "Accepted" : "Pending";

    public string ExpiryLabel => ExpiresAt is null
        ? "no expiry"
        : $"expires {ExpiresAt.Value.ToLocalTime():d}";

    /// <summary>Role · status · expiry, as the pending-invites row shows it.</summary>
    public string MetaLine => $"{RoleLabel} · {StatusLabel} · {ExpiryLabel}";

    /// <summary>False for the envelope returned by a create call, which carries no token.</summary>
    public bool CanRevoke => !string.IsNullOrEmpty(Token);
}
