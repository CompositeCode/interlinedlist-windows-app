using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Moderation & safety: block, mute, and report users (report-a-message lives in
/// the Messages partial). Status probes read the { "blocked": bool } /
/// { "muted": bool } shapes verified live 2026-07-31. All accept the bearer token.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task BlockUserAsync(string username, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/users/{Uri.EscapeDataString(username)}/block", new { }, ct);

    public Task UnblockUserAsync(string username, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/users/{Uri.EscapeDataString(username)}/block", null, ct);

    public async Task<bool> IsBlockingAsync(string username, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/users/{Uri.EscapeDataString(username)}/block", ct);
        return json.TryGetProperty("blocked", out var b) && b.ValueKind == JsonValueKind.True;
    }

    public Task MuteUserAsync(string username, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/users/{Uri.EscapeDataString(username)}/mute", new { }, ct);

    public Task UnmuteUserAsync(string username, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/users/{Uri.EscapeDataString(username)}/mute", null, ct);

    public async Task<bool> IsMutingAsync(string username, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/users/{Uri.EscapeDataString(username)}/mute", ct);
        return json.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;
    }

    public Task ReportUserAsync(string username, string reason, string? detail, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/users/{Uri.EscapeDataString(username)}/report", new { reason, detail }, ct);

    public async Task<List<ModeratedUser>> GetBlockedUsersAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user/blocks", ct);
        return json.TryGetProperty("blockedUsers", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ModeratedUser>>(JsonOptions) ?? new()
            : new();
    }

    public async Task<List<ModeratedUser>> GetMutedUsersAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/user/mutes", ct);
        return json.TryGetProperty("mutedUsers", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ModeratedUser>>(JsonOptions) ?? new()
            : new();
    }
}
