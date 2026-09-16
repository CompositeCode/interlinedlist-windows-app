using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// What <c>POST /api/lists</c> answers when <c>source: "github"</c> —
/// <c>{ message, data, refreshStatus }</c>, where <c>refreshStatus</c> is
/// documented as "present only for GitHub-backed lists".
///
/// <para>
/// <b>The GitHub-backed create was deliberately never executed.</b> Creating one
/// wires a real GitHub repository to a real list on shared test
/// infrastructure, so this app's create path is built and left unexercised. What
/// <em>was</em> verified live on 2026-09-16, without creating anything:
/// </para>
/// <list type="bullet">
/// <item><description><c>POST /api/lists { title, source: "github" }</c> with no
/// <c>githubRepo</c> → <b>400</b> <c>{ "error": "githubRepo is required for
/// GitHub-backed lists (format: owner/repo)", "code": "bad_request" }</c> — and
/// nothing was created (a following <c>GET /api/lists</c> still showed exactly
/// the one pre-existing list). So the server does recognise
/// <c>source: "github"</c>, and it wants <c>owner/repo</c>.</description></item>
/// <item><description>The plain local create returns
/// <c>{ "message": "List created successfully", "data": { …, "source": "local",
/// "githubRepo": null, "githubRepoPrivate": null, "properties": [] } }</c> with
/// <b>no</b> <c>refreshStatus</c> key, matching the spec's "present only for
/// GitHub-backed lists".</description></item>
/// </list>
/// <para>
/// Hence <see cref="RefreshStatus"/> is nullable and <see cref="Raw"/> is kept:
/// per this repo's read-after-write rule the caller re-reads the list rather than
/// trusting this envelope.
/// </para>
/// </summary>
public sealed class GitHubBackedListCreated
{
    /// <summary>The full response body.</summary>
    public JsonElement Raw { get; init; }

    /// <summary>Server confirmation, e.g. "List created successfully".</summary>
    public string? Message { get; init; }

    /// <summary>
    /// The new list's GitHub backing, read straight out of the <c>data</c> object.
    /// Null if the server answered without one.
    /// </summary>
    public GitHubListBacking? Backing { get; init; }

    /// <summary>
    /// <c>refreshStatus</c> — the initial issue sync's outcome. Unobserved (see
    /// the class remarks), so treat a null as "no status reported", not as
    /// failure.
    /// </summary>
    public string? RefreshStatus { get; init; }

    /// <summary>The created list's id, when the server returned one.</summary>
    public string? ListId => Backing?.ListId is { Length: > 0 } id ? id : null;

    public static GitHubBackedListCreated FromJson(JsonElement body)
    {
        string? Str(JsonElement obj, string name) =>
            obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        GitHubListBacking? backing = null;
        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object)
        {
            backing = GitHubListBacking.FromListJson(data);
        }

        return new GitHubBackedListCreated
        {
            Raw = body,
            Message = Str(body, "message"),
            RefreshStatus = Str(body, "refreshStatus"),
            Backing = backing
        };
    }
}
