using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// 1:1 direct messages. The MVP conversation list is sourced from
/// GET /api/dm/recipients (people you can DM); message history comes from the
/// per-user thread endpoint. NOTE: the POST /api/dm request body is undocumented
/// in the OpenAPI spec, so <see cref="SendDmAsync"/> uses the inferred
/// { recipientId, body } shape and is read-after-write — re-fetch the thread and
/// verify the field names against a real send before depending on it.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<List<DmRecipient>> GetDmRecipientsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/dm/recipients", ct);
        return json.TryGetProperty("recipients", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<DmRecipient>>(JsonOptions) ?? new()
            : new();
    }

    public Task<DmThread> GetDmThreadAsync(string username, CancellationToken ct = default)
        => GetJsonAsync<DmThread>($"api/dm/thread/{Uri.EscapeDataString(username)}", ct);

    public async Task<int> GetDmUnreadCountAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/dm/unread-count", ct);
        return json.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 0;
    }

    public Task SendDmAsync(string recipientId, string body, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/dm", new { recipientId, body }, ct);

    public Task MarkDmReadAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/dm/{id}/read", new { }, ct);

    public Task TrashDmAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/dm/{id}/trash", new { }, ct);
}
