using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One organization from GET /api/github/orgs, used to group/filter the repo
/// picker (<c>GET /api/github/repos?org=&lt;login&gt;</c>).
/// <para>
/// <b>Shape NOT live-verified.</b> The endpoint answered 200 with a bare empty
/// array <c>[]</c> on the test account (probed 2026-09-15): the linked GitHub
/// identity there — <c>InterlinedListMessenger</c> — belongs to no
/// organizations, so no item was ever observed. Every field is therefore
/// nullable and nothing is <c>required</c>, so a slim server projection (the
/// repos endpoint returns one) deserializes without throwing. The client also
/// accepts a bare array of login strings, in case the server projects it that
/// far down — see <c>InterlinedApiClient.GetGitHubOrgsAsync</c>. Re-probe with
/// an account that is in an org before typing this strictly.
/// </para>
/// </summary>
public sealed class GitHubOrg
{
    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Best label available: display name, else login, else "unknown".</summary>
    public string DisplayName =>
        Name is { Length: > 0 } name ? name
        : Login is { Length: > 0 } login ? login
        : "unknown";

    /// <summary>
    /// The value to pass as <c>?org=</c>. Empty when the server gave us no
    /// login, in which case the caller must not attempt a filtered repo fetch.
    /// </summary>
    public string FilterValue => Login ?? string.Empty;
}
