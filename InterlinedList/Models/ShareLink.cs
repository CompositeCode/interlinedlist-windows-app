namespace InterlinedList.Models;

/// <summary>
/// A tokenized public share link for a list or document. Shape verified live
/// 2026-08-01: POST returns { token, url, role, expiresAt }; GET adds
/// createdAt / revokedAt. Revoke with DELETE …/share-links/{token}.
/// </summary>
public sealed class ShareLink
{
    public required string Token { get; init; }
    public string? Url { get; init; }
    public string? Role { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }

    public bool IsActive => RevokedAt is null;
    public string RoleLabel => string.IsNullOrEmpty(Role) ? "viewer" : Role;
}
