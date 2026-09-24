using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// One repository from GET /api/github/repos.
/// <para>
/// This is a <b>server-side slim projection</b>, not GitHub's own repository
/// object: every item carries exactly four keys — <c>full_name</c>,
/// <c>name</c>, <c>private</c>, <c>owner_login</c> (verified live 2026-09-15
/// across 559 + 2000 + 278 items from <c>?org=github</c>, <c>?org=microsoft</c>
/// and <c>?org=dotnet</c>; the key set was uniform over all of them). In
/// particular <c>owner_login</c> is flattened by InterlinedList — GitHub's real
/// API nests an <c>owner</c> object there — and there is no id, description,
/// default branch, or timestamp. Don't add fields here without re-probing.
/// </para>
/// <para>
/// The GitHub-proxy endpoints return raw snake_case GitHub-flavoured JSON,
/// unlike the rest of the InterlinedList API (camelCase, which the client's
/// <c>JsonSerializerDefaults.Web</c> options map automatically). Hence the
/// explicit <see cref="JsonPropertyNameAttribute"/> on every property of the
/// GitHub wire models — case-insensitive matching does not bridge underscores.
/// </para>
/// </summary>
public sealed class GitHubRepo
{
    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The owner's login, flattened by the server from GitHub's nested owner object.</summary>
    [JsonPropertyName("owner_login")]
    public string? OwnerLogin { get; init; }

    /// <summary>
    /// Whether the repository is private <em>on GitHub</em>. Unrelated to whether
    /// a GitHub-backed InterlinedList list built from it is public — the two are
    /// set separately (see the "Repository link under the title" section of
    /// /help/lists).
    /// </summary>
    [JsonPropertyName("private")]
    public bool IsPrivate { get; init; }

    /// <summary>Owner login, falling back to the <c>owner/repo</c> prefix of <see cref="FullName"/>.</summary>
    public string Owner =>
        OwnerLogin is { Length: > 0 } login ? login
        : FullName.IndexOf('/') is var i && i > 0 ? FullName[..i]
        : string.Empty;

    public string VisibilityLabel => IsPrivate ? "Private" : "Public";

    /// <summary>The repository's issues page, for the "owner/repo issues" link the web app shows.</summary>
    public string IssuesUrl => $"https://github.com/{FullName}/issues";
}
