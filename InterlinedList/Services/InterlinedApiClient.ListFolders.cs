using System.Net.Http;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// List folders — <c>/api/folders</c>.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from document folders (<c>/api/documents/folders</c>), which the app
/// already implements: different endpoints, different ids, no overlap. This
/// domain was entirely missing.
/// </para>
/// <para>
/// All four operations verified live 2026-09-16 against throwaway folders
/// (since deleted, account confirmed restored). Envelopes:
/// </para>
/// <code>
/// GET    /api/folders      -> 200 {"folders":[{id,name,parentId}, …]}    // flat
/// POST   /api/folders      -> 201 {"message":"Folder created successfully","folder":{id,name,parentId}}
/// PUT    /api/folders/{id} -> 200 {"message":"Folder updated successfully","folder":{…}}
/// DELETE /api/folders/{id} -> 200
/// </code>
/// <para>
/// <b>⚠️ The server does NOT guard against cycles.</b> Moving a folder into its
/// own descendant returns <c>200</c> and creates a loop — verified by moving a
/// parent into its own child. A tree renderer walking <c>parentId</c> would
/// then recurse forever. <see cref="WouldCreateCycle"/> is therefore not
/// defensive politeness; it is the only thing preventing it.
/// </para>
/// </remarks>
public sealed partial class InterlinedApiClient
{
    public async Task<List<ListFolder>> GetListFoldersAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/folders", ct);
        return json.TryGetProperty("folders", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ListFolder>>(JsonOptions) ?? []
            : [];
    }

    /// <summary>Create a folder. Subscribers only — a free account gets 402/403.</summary>
    public async Task<ListFolder?> CreateListFolderAsync(
        string name, string? parentId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        // Only send parentId when there is one. An explicit null is a 500 on
        // some writers on this API (see #171), so don't risk it.
        if (parentId is { Length: > 0 }) body["parentId"] = parentId;

        var json = await SendJsonAsync<JsonElement>(HttpMethod.Post, "api/folders", body, ct);
        return ReadFolder(json);
    }

    /// <summary>
    /// Rename and/or move a folder. Pass only what is changing.
    /// </summary>
    /// <param name="parentId">
    /// New parent. Pass <see cref="MoveToRoot"/> to move to the top level —
    /// an explicit JSON <c>null</c> is what the server wants there, and it was
    /// verified to work.
    /// </param>
    public async Task<ListFolder?> UpdateListFolderAsync(
        string id, string? name = null, string? parentId = null,
        bool moveToRoot = false, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (name is { Length: > 0 }) body["name"] = name;
        if (moveToRoot) body["parentId"] = null;
        else if (parentId is { Length: > 0 }) body["parentId"] = parentId;

        var json = await SendJsonAsync<JsonElement>(HttpMethod.Put, $"api/folders/{id}", body, ct);
        return ReadFolder(json);
    }

    /// <summary>Sentinel for <c>UpdateListFolderAsync(moveToRoot: true)</c> readability.</summary>
    public const bool MoveToRoot = true;

    /// <summary>
    /// Soft-delete a folder <b>and its subfolders</b>. Confirm before calling —
    /// the cascade is not obvious from the name.
    /// </summary>
    public Task DeleteListFolderAsync(string id, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/folders/{id}", null, ct);

    /// <summary>
    /// Assign a list to a folder, or clear it with <paramref name="folderId"/>
    /// null.
    /// </summary>
    /// <remarks>
    /// <c>folderId</c> is <b>not</b> among the eleven fields
    /// <c>POST /api/lists</c> accepts, so a new list cannot be created straight
    /// into a folder — it has to be moved afterward. Verified live:
    /// <c>PUT /api/lists/{id}</c> with <c>{"folderId": …}</c> returns
    /// <c>200</c> and the list's <c>folderId</c> reflects it, and
    /// <c>{"folderId": null}</c> clears it. See #179.
    /// </remarks>
    public Task SetListFolderAsync(string listId, string? folderId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/lists/{listId}",
            new Dictionary<string, object?> { ["folderId"] = folderId }, ct);

    private static ListFolder? ReadFolder(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        // Create/update wrap the object under "folder"; tolerate a bare object.
        var node = json.TryGetProperty("folder", out var wrapped) ? wrapped : json;
        return node.ValueKind == JsonValueKind.Object
            ? node.Deserialize<ListFolder>(JsonOptions)
            : null;
    }

    /// <summary>
    /// True when moving <paramref name="folderId"/> under
    /// <paramref name="newParentId"/> would create a loop.
    /// </summary>
    /// <remarks>
    /// <b>The server permits this</b> — verified live, moving a parent into its
    /// own child returned <c>200</c> and produced a genuine cycle. Nothing
    /// server-side prevents it, so every move must be checked here first or a
    /// tree walk will hang.
    /// </remarks>
    public static bool WouldCreateCycle(
        IReadOnlyCollection<ListFolder> folders, string folderId, string? newParentId)
    {
        if (newParentId is null or "") return false;      // moving to root is always safe
        if (newParentId == folderId) return true;          // its own parent

        var byId = folders.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var cursor = newParentId;
        // Bounded by the folder count, so a pre-existing cycle in the data
        // can't hang this check either.
        for (var hops = 0; hops <= folders.Count && cursor is { Length: > 0 }; hops++)
        {
            if (cursor == folderId) return true;
            cursor = byId.TryGetValue(cursor, out var parent) ? parent.ParentId : null;
        }
        return false;
    }
}
