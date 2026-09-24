using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// People: a user's public profile and their public content. Relationship state
/// (follow / block / mute) is NOT on the profile payload — read it from the
/// follow-status and moderation endpoints (see the Follow/Moderation partials).
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task<UserProfile> GetProfileAsync(string username, CancellationToken ct = default)
        => GetJsonAsync<UserProfile>($"api/users/{Uri.EscapeDataString(username)}", ct);

    public Task<MessagesPage> GetUserMessagesAsync(string username, int limit = 20, int offset = 0, CancellationToken ct = default)
        => GetJsonAsync<MessagesPage>($"api/user/{Uri.EscapeDataString(username)}/messages?limit={limit}&offset={offset}", ct);

    public Task<ListsPage> GetUserListsAsync(string username, CancellationToken ct = default)
        => GetJsonAsync<ListsPage>($"api/users/{Uri.EscapeDataString(username)}/lists", ct);

    /// <summary>
    /// Resolve a handle to a user, or null when there is no such account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GET /api/users/lookup?handle=</c>. The query parameter is
    /// <b><c>handle</c></b> — calling it <c>username</c> returns
    /// <c>400 {"error":"missing_handle","code":"bad_request"}</c>.
    /// </para>
    /// <para>
    /// <b>It takes a bare username only.</b> Verified live 2026-09-16:
    /// </para>
    /// <code>
    /// handle=adron                -> 200 {id,username,displayName,avatar,isPrivate}
    /// handle=@adron               -> 404 {"error":"user_not_found","code":"not_found"}
    /// handle=adron@metalhead.club -> 404
    /// handle=adron.bsky.social    -> 404
    /// </code>
    /// <para>
    /// So it is a plain username resolver, <i>not</i> a cross-platform handle
    /// resolver. A leading <c>@</c> is stripped here rather than at each call
    /// site, since users type and paste handles with it.
    /// </para>
    /// <para>
    /// Note the spec lists only <c>200</c>/<c>401</c>/<c>500</c> for this
    /// operation, but <c>404</c> is real and is the normal "no such user"
    /// answer — it is returned as null rather than thrown.
    /// </para>
    /// </remarks>
    public async Task<UserSearchResult?> LookupUserAsync(string handle, CancellationToken ct = default)
    {
        var bare = handle?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(bare))
            return null;

        try
        {
            return await GetJsonAsync<UserSearchResult>(
                $"api/users/lookup?handle={Uri.EscapeDataString(bare)}", ct);
        }
        catch (InterlinedApiException ex) when (ex.StatusCode == 404)
        {
            // "No such user" is an expected answer, not a failure.
            return null;
        }
    }
}
