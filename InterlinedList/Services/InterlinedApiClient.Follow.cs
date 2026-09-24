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

    public Task<List<FollowUser>> GetMutualAsync(string userId, CancellationToken ct = default)
        => GetUserArrayAsync($"api/follow/{userId}/mutual", "mutual", ct);

    // Follow lists wrap their array under different property names
    // (requests/followers/following/mutual) — pull the named array, tolerate a miss.
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
