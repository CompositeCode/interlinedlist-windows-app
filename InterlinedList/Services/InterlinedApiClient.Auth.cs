using System.Net.Http;

namespace InterlinedList.Services;

/// <summary>
/// Pre-login self-service: account registration and password reset. Both are
/// public (no bearer). Request shapes verified against the OpenAPI spec
/// 2026-08-01: register { email, username, password, displayName }, forgot
/// { email }. Responses aren't parsed — on success the caller either logs in
/// with the new credentials or tells the user to check their inbox.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task RegisterAsync(string email, string username, string password, string? displayName, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/register",
            new { email, username, password, displayName }, ct);

    public Task ForgotPasswordAsync(string email, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/auth/forgot-password", new { email }, ct);
}
