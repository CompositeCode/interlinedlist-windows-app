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
}
