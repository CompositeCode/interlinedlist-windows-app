using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// A list, from <c>GET /api/lists</c> and <c>GET /api/users/{username}/lists</c>.
/// </summary>
/// <remarks>
/// Reconciled against live payloads 2026-09-16 (14 lists sampled across both
/// endpoints, so that <c>source: "github"</c> rows and a populated
/// <see cref="Parent"/> both appeared with real values).
/// </remarks>
public sealed class ListSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Owner. Drives owner-only affordances — don't infer ownership.</summary>
    public string? UserId { get; init; }

    /// <summary>Set when the list was created from a message ("Create from…").</summary>
    public string? MessageId { get; init; }

    /// <summary>Parent list id; lists form a tree.</summary>
    public string? ParentId { get; init; }

    /// <summary>Containing list folder (<c>/api/folders</c>), or null at root.</summary>
    public string? FolderId { get; init; }

    /// <summary>
    /// Backing source: <c>local</c> or <c>github</c>. Live sample was 9 local /
    /// 5 github, so both paths are real on a normal account.
    /// </summary>
    public string? Source { get; init; }

    /// <summary><c>owner/repo</c> for a GitHub-backed list.</summary>
    public string? GithubRepo { get; init; }

    /// <summary>
    /// Whether the backing <b>repository on GitHub</b> is private — not whether
    /// this list is private; the two are set separately.
    /// <para>
    /// <b>Nullable on purpose.</b> Per the product docs, a list created before
    /// this field existed shows <i>no</i> tag until its first sync, because an
    /// unrecorded value must never be presented as public. Treat null as
    /// "unknown", never as false.
    /// </para>
    /// </summary>
    public bool? GithubRepoPrivate { get; init; }

    /// <summary>Soft-delete marker; non-null means deleted.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>
    /// Opaque server-side metadata. Raw JSON so it round-trips untouched — it was
    /// null across every sampled list, so its shape is unverified.
    /// </summary>
    public JsonElement? Metadata { get; init; }

    /// <summary>
    /// The immediate parent, inlined as <c>{ id, title }</c> when this list has
    /// one. Enough to render a breadcrumb without a second fetch; the full
    /// ancestor chain comes from <c>GET /api/users/{username}/lists/{id}</c>.
    /// </summary>
    public ListRef? Parent { get; init; }

    /// <summary>Immediate child lists, inlined by the server (often empty).</summary>
    public List<ListRef>? Children { get; init; }

    // ── Derived ─────────────────────────────────────────────────────────────────

    /// <summary>Backed by GitHub Issues — rows map to issues.</summary>
    public bool IsGitHubBacked =>
        string.Equals(Source, "github", StringComparison.OrdinalIgnoreCase)
        || GithubRepo is { Length: > 0 };

    /// <summary>Show the "Private repo" tag only when the server actually recorded it.</summary>
    public bool ShowPrivateRepoTag => IsGitHubBacked && GithubRepoPrivate == true;

    /// <summary>The repository's issues page on GitHub, for the under-title link.</summary>
    public string? GithubIssuesUrl =>
        GithubRepo is { Length: > 0 } repo ? $"https://github.com/{repo}/issues" : null;

    public bool IsDeleted => DeletedAt is not null;
    public bool HasParent => ParentId is { Length: > 0 };
    public bool HasChildren => Children is { Count: > 0 };
    public bool IsInFolder => FolderId is { Length: > 0 };

    /// <summary>Created by converting a message.</summary>
    public bool CreatedFromMessage => MessageId is { Length: > 0 };
}

/// <summary>A minimal list reference, as inlined in <c>parent</c> / <c>children</c>.</summary>
public sealed class ListRef
{
    public required string Id { get; init; }
    public string? Title { get; init; }
}
