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
}
