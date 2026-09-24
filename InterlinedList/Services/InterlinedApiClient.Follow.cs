using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The social graph: follow/unfollow, pending follow requests (for private
/// accounts), and follower/following/mutual lists. Follow counts live in the
/// core partial (<see cref="GetFollowCountsAsync"/>). All of these accept the
/// bearer token (verified live 2026-07-31).
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task FollowAsync(string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/follow/{userId}", new { }, ct);

    public Task UnfollowAsync(string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/follow/{userId}", null, ct);

    public Task<FollowStatus> GetFollowStatusAsync(string userId, CancellationToken ct = default)
        => GetJsonAsync<FollowStatus>($"api/follow/{userId}/status", ct);

    public Task<List<FollowUser>> GetFollowRequestsAsync(CancellationToken ct = default)
        => GetUserArrayAsync("api/follow/requests", "requests", ct);

    public Task ApproveFollowAsync(string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/follow/{userId}/approve", new { }, ct);

    public Task RejectFollowAsync(string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/follow/{userId}/reject", new { }, ct);

    public Task RemoveFollowerAsync(string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/follow/{userId}/remove", null, ct);

    public Task<List<FollowUser>> GetFollowersAsync(string userId, CancellationToken ct = default)
        => GetUserArrayAsync($"api/follow/{userId}/followers", "followers", ct);

    public Task<List<FollowUser>> GetFollowingAsync(string userId, CancellationToken ct = default)
        => GetUserArrayAsync($"api/follow/{userId}/following", "following", ct);

    /// <summary>
    /// Mutual-follow <b>counts</b> between the caller and <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a <c>Task&lt;List&lt;FollowUser&gt;&gt;</c> signature that the API
    /// cannot satisfy. The endpoint returns
    /// <c>{"mutualFollowers":n,"mutualFollowing":n}</c> — never a user array —
    /// so the old call asked <see cref="GetUserArrayAsync"/> for an absent
    /// <c>mutual</c> key and silently received an empty list, which is why the
    /// profile's "Mutual connections" chips never rendered. See #160.
    /// </para>
    /// <para>
    /// There is <b>no</b> endpoint that lists the mutual users — both this
    /// route's parameter forms (bare, and with the spec-documented
    /// <c>otherUserId</c>) return the same counts object.
    /// </para>
    /// </remarks>
    public async Task<MutualFollowCounts> GetMutualCountsAsync(string userId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/follow/{userId}/mutual", ct);
        return json.ValueKind == JsonValueKind.Object
            ? json.Deserialize<MutualFollowCounts>(JsonOptions) ?? MutualFollowCounts.None
            : MutualFollowCounts.None;
    }
    /// <summary>One page of a user's followers.</summary>
    /// <param name="status">
    /// Optional server-side filter on the follow edge's state. Live values seen:
    /// <c>approved</c>; <c>pending</c> is accepted and returns an empty page on an
    /// account with no requests. Documented in the spec alongside <c>limit</c>.
    /// </param>
    /// <remarks>
    /// Verified live 2026-09-16: the envelope is
    /// <c>{followers:[…], pagination:{total,limit,offset,hasMore}}</c>, the
    /// server default limit is <b>50</b>, and <c>limit</c>+<c>offset</c> both
    /// work (<c>?limit=2&amp;offset=1</c> returned rows 2–3 of 8 with
    /// <c>hasMore:true</c>). Note <c>offset</c> is functional but <b>not</b> in
    /// the spec's parameter list — same situation as <c>GET /api/messages</c>.
    /// </remarks>
    public Task<FollowUserPage> GetFollowersPageAsync(
        string userId, int limit = 50, int offset = 0, string? status = null, CancellationToken ct = default)
        => GetUserPageAsync($"api/follow/{userId}/followers", "followers", limit, offset, status, ct);

    /// <summary>One page of the accounts a user follows.</summary>
    public Task<FollowUserPage> GetFollowingPageAsync(
        string userId, int limit = 50, int offset = 0, string? status = null, CancellationToken ct = default)
        => GetUserPageAsync($"api/follow/{userId}/following", "following", limit, offset, status, ct);

    // Follow lists wrap their array under different property names — pull the
    // named array, tolerate a miss.
    //
    // Keys verified live 2026-09-16: `requests` -> {"requests":[]},
    // `followers`/`following` -> {"<key>":[…],"pagination":{…}}. NOTE `mutual`
    // is NOT one of these — that endpoint returns counts, not an array, and
    // this helper's tolerance silently hid the mismatch for weeks (#160). If you
    // add a key here, confirm the array actually exists in a live payload; a
    // wrong key fails as "feature renders nothing", not as an error.
    private async Task<List<FollowUser>> GetUserArrayAsync(string path, string property, CancellationToken ct)
    {
        var json = await GetElementAsync(path, ct);
        return json.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<FollowUser>>(JsonOptions) ?? new()
            : new();
    }

    // Same envelope, but keeping the pagination block so a caller can page
    // rather than silently taking the server's first 50.
    private async Task<FollowUserPage> GetUserPageAsync(
        string path, string property, int limit, int offset, string? status, CancellationToken ct)
    {
        var query = $"?limit={limit}&offset={offset}";
        if (status is { Length: > 0 })
            query += $"&status={Uri.EscapeDataString(status)}";

        var json = await GetElementAsync(path + query, ct);

        var users = json.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<FollowUser>>(JsonOptions) ?? []
            : [];

        var pagination = json.TryGetProperty("pagination", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.Deserialize<Pagination>(JsonOptions)
            : null;

        return new FollowUserPage
        {
            Users = users,
            Total = pagination?.Total ?? users.Count,
            // hasMore is authoritative; fall back to a full page meaning "maybe more".
            HasMore = pagination?.HasMore ?? users.Count >= limit,
        };
    }
}
