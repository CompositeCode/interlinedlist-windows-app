using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// Lists (Airtable-like typed-column data). Rows are freeform JSON on a list
/// with no schema — still true and still supported (verified live 2026-09-16:
/// a list created without a schema answers GET …/schema with
/// <c>"fields": []</c> and accepts rows with arbitrary, even nested, keys) —
/// but the schema DSL is now fully documented at /help/api/lists-dsl and
/// implemented below, superseding the old "never fully confirmed live" note.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public async Task<ListsPage> GetListsAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/lists?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ListsPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/lists returned no body.");
    }

    /// <summary>
    /// Create a list, optionally with its columns already defined.
    /// <paramref name="schema"/> is validated the same way as the rebuild PUT
    /// (verified live: an invalid type here returns the identical
    /// <c>400 Invalid schema: Field 'y' has invalid type 'integer'…</c>), so a
    /// bad schema is caught client-side first and reported as a
    /// <see cref="ListSchemaException"/>. Passing null keeps the historical
    /// schema-less behaviour — the server tolerates an explicit
    /// <c>"schema": null</c> (verified live, 201).
    /// </summary>
    public async Task<ListSummary> CreateListAsync(
        string title, string? description, ListSchema? schema = null, CancellationToken ct = default)
    {
        if (schema is not null && schema.Validate() is { Count: > 0 } issues)
            throw ListSchemaException.FromIssues(issues);

        using var resp = await SendAsync(HttpMethod.Post, "api/lists", new { title, description, schema }, ct);
        if (schema is not null && !resp.IsSuccessStatusCode)
            throw await ListSchemaException.FromResponseAsync(resp, ct);
        await EnsureSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return json.GetProperty("data").Deserialize<ListSummary>(JsonOptions)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "POST /api/lists returned no data.");
    }

    public async Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Delete, $"api/lists/{listId}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<ListDataPage> GetListDataAsync(string listId, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"api/lists/{listId}/data?limit={limit}&offset={offset}", body: null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ListDataPage>(JsonOptions, ct)
            ?? throw new InterlinedApiException((int)resp.StatusCode, "GET /api/lists/{id}/data returned no body.");
    }

    public async Task AddListRowAsync(string listId, Dictionary<string, object?> rowData, CancellationToken ct = default)
    {
        // Write-path response shape wasn't fully verified live — re-fetch rows afterward instead of parsing this.
        using var resp = await SendAsync(HttpMethod.Post, $"api/lists/{listId}/data", new { data = rowData }, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // GET /api/lists/{id} returns the list metadata under a "data" envelope
    // (verified live 2026-07-31).
    public async Task<ListSummary> GetListAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}", ct);
        return json.GetProperty("data").Deserialize<ListSummary>(JsonOptions)
            ?? throw new InterlinedApiException(200, "GET /api/lists/{id} returned no data.");
    }

    public Task UpdateListAsync(string listId, string title, string? description, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/lists/{listId}", new { title, description }, ct);

    // Edit/delete of an individual row — the pieces that made rows write-once
    // before. Same read-after-write discipline as AddListRowAsync.
    public Task UpdateListRowAsync(string listId, string rowId, Dictionary<string, object?> rowData, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Put, $"api/lists/{listId}/data/{rowId}", new { data = rowData }, ct);

    public Task DeleteListRowAsync(string listId, string rowId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/lists/{listId}/data/{rowId}", null, ct);

    // ── Schema / columns ───────────────────────────────────────────────────────
    // PUT /api/lists/{id}/schema is ONE route with TWO bodies that do very
    // different things, so it is deliberately TWO methods here (a single
    // "SaveSchema" would be a data-loss footgun):
    //
    //   RebuildListSchemaDestructiveAsync → { schema: {…} }     DESTRUCTIVE
    //   UpdateListPropertiesAsync         → { properties: […] } non-destructive
    //
    // Both behaviours were verified live 2026-09-16 on a throwaway list (since
    // deleted). What "destructive" actually costs, measured:
    //   * every column row is dropped and recreated with a NEW id, losing any
    //     validation / options / visibility / helpText the new DSL omits;
    //   * schema.name OVERWRITES the list's title and schema.description
    //     overwrites its description (null clears it);
    //   * the ROWS themselves survive, but values whose key no longer has a
    //     column are orphaned — still in rowData, no longer shown or validated.
    // The properties form, by contrast, left both rows and their untouched
    // column metadata exactly as they were.

    /// <summary>
    /// Read a list's columns as DSL. Envelope verified live:
    /// <c>200 { "data": { "name": …, "description"?: …, "fields": [ … ] } }</c>.
    /// A list with no schema yields <c>fields: []</c> — a valid state that
    /// cannot be PUT back (see <see cref="ListSchema.Validate"/>).
    /// </summary>
    public async Task<ListSchema> GetListSchemaAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/schema", ct);
        return json.GetProperty("data").Deserialize<ListSchema>(JsonOptions)
            ?? throw new InterlinedApiException(200, "GET /api/lists/{id}/schema returned no data.");
    }

    /// <summary>
    /// The same columns in their stored <see cref="ListProperty"/> form, read
    /// from <c>GET /api/lists/{id}</c> → <c>data.properties[]</c>. This is the
    /// only source of each column's id, so it is the required first step of a
    /// non-destructive edit: read → <see cref="ListProperty.ToUpdate"/> →
    /// mutate → <see cref="UpdateListPropertiesAsync"/>.
    /// </summary>
    public async Task<List<ListProperty>> GetListPropertiesAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}", ct);
        return json.TryGetProperty("data", out var data)
               && data.TryGetProperty("properties", out var arr)
               && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ListProperty>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// DESTRUCTIVE. Wipes and recreates every column from <paramref name="schema"/>,
    /// and overwrites the list's title (and description) from the schema's own
    /// name/description. Rows are kept but values for dropped keys are orphaned.
    /// Use <see cref="UpdateListPropertiesAsync"/> for label renames and single
    /// add/remove/reorder edits; only come here to re-shape a list wholesale or
    /// to introduce one of the six types the properties form cannot express.
    ///
    /// Returns the updated list (the server answers
    /// <c>{ message, data: { …list…, properties: [ … ] } }</c>) so the caller can
    /// see the new title; re-read <see cref="GetListSchemaAsync"/> for the
    /// rebuilt columns.
    /// Throws <see cref="ListSchemaException"/> — not <see cref="InterlinedApiException"/> —
    /// with per-column <see cref="ListSchemaIssue"/>s.
    /// </summary>
    public async Task<ListSummary> RebuildListSchemaDestructiveAsync(
        string listId, ListSchema schema, CancellationToken ct = default)
    {
        if (schema.Validate() is { Count: > 0 } issues)
            throw ListSchemaException.FromIssues(issues);

        var json = await SendSchemaAsync(HttpMethod.Put, $"api/lists/{listId}/schema", new { schema }, ct);
        return json.GetProperty("data").Deserialize<ListSummary>(JsonOptions)
            ?? throw new ListSchemaException(200, "PUT /api/lists/{id}/schema returned no data.");
    }

    /// <summary>
    /// Non-destructive column edit — ROW DATA IS PRESERVED (verified live: two
    /// rows came through a label rename plus a column add with identical ids,
    /// version numbers and rowData). Items carrying an id are updated in place,
    /// items without one are created, and any existing column the array omits is
    /// deleted with its key stripped from every row.
    ///
    /// Pass every column you intend to keep, in the order you want them —
    /// displayOrder is renumbered 0..n-1 from the array. Every item needs a
    /// <c>propertyType</c> from <see cref="ListFieldType.PropertiesEditable"/>;
    /// the other six DSL types are rejected outright here, so a list using them
    /// can only be re-shaped through the destructive rebuild (that guard is
    /// applied client-side first, as a <see cref="ListSchemaException"/>).
    ///
    /// <paramref name="force"/> confirms deleting columns that still hold data;
    /// without it the server answers 409 and the thrown
    /// <see cref="ListSchemaException"/> has <see cref="ListSchemaException.RequiresForce"/>
    /// set and names the blocked columns in
    /// <see cref="ListSchemaException.PropertiesWithData"/>.
    ///
    /// Returns the stored columns in displayOrder, from the verified
    /// <c>{ "properties": [ … ] }</c> envelope (no data wrapper).
    /// </summary>
    public async Task<List<ListProperty>> UpdateListPropertiesAsync(
        string listId, IEnumerable<ListPropertyUpdate> properties, bool force = false, CancellationToken ct = default)
    {
        var items = properties.ToList();
        if (ValidateProperties(items) is { Count: > 0 } issues)
            throw ListSchemaException.FromIssues(issues);

        var path = $"api/lists/{listId}/schema" + (force ? "?force=true" : string.Empty);
        var json = await SendSchemaAsync(HttpMethod.Put, path, new { properties = items }, ct);
        return json.TryGetProperty("properties", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ListProperty>>(JsonOptions) ?? new()
            : new();
    }

    /// <summary>
    /// Client-side mirror of the structured form's own 400s, so the UI can
    /// attach them to a column instead of round-tripping for the message.
    /// </summary>
    private static List<ListSchemaIssue> ValidateProperties(List<ListPropertyUpdate> items)
    {
        var issues = new List<ListSchemaIssue>();

        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];

            if (string.IsNullOrWhiteSpace(p.PropertyKey))
                issues.Add(new ListSchemaIssue($"Column at index {i} must have a propertyKey.", null, i));

            if (string.IsNullOrWhiteSpace(p.PropertyName))
                issues.Add(new ListSchemaIssue(
                    $"Column '{p.PropertyKey}' must have a propertyName (its label).", p.PropertyKey, i));

            if (!ListFieldType.SupportsPropertiesEdit(p.PropertyType))
                issues.Add(new ListSchemaIssue(
                    $"Unknown propertyType '{p.PropertyType}'. " +
                    $"Allowed: {string.Join(", ", ListFieldType.PropertiesEditable)}", p.PropertyKey, i));
        }

        foreach (var group in items
                     .Where(p => !string.IsNullOrWhiteSpace(p.PropertyKey))
                     .GroupBy(p => p.PropertyKey, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            issues.Add(new ListSchemaIssue($"Duplicate propertyKey '{group.Key}' in request", group.Key));
        }

        return issues;
    }

    /// <summary>
    /// Schema writes bypass the shared EnsureSuccessAsync because it keeps only
    /// status + message and would drop a 409's <c>propertiesWithData</c> array.
    /// </summary>
    private async Task<JsonElement> SendSchemaAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var resp = await SendAsync(method, path, body, ct);
        if (!resp.IsSuccessStatusCode)
            throw await ListSchemaException.FromResponseAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    /// <summary>
    /// Lists owned by others that have been shared with the current user
    /// (GET /api/lists/watching → { lists, pagination }). Their rows are readable
    /// via <see cref="GetListDataAsync"/> (access is granted server-side).
    /// </summary>
    public async Task<List<WatchedList>> GetWatchingListsAsync(CancellationToken ct = default)
    {
        var json = await GetElementAsync("api/lists/watching", ct);
        return json.TryGetProperty("lists", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<WatchedList>>(JsonOptions) ?? new()
            : new();
    }

    // ── Share links (create a public read link for a list) ──────────────────────
    // Shape verified live 2026-08-01: GET → { shareLinks }, POST → the new link,
    // DELETE …/{token} revokes it.

    public async Task<List<ShareLink>> GetListShareLinksAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/share-links", ct);
        return json.TryGetProperty("shareLinks", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<ShareLink>>(JsonOptions) ?? new()
            : new();
    }

    public Task<ShareLink> CreateListShareLinkAsync(string listId, CancellationToken ct = default)
        => SendJsonAsync<ShareLink>(HttpMethod.Post, $"api/lists/{listId}/share-links", new { }, ct);

    public Task DeleteListShareLinkAsync(string listId, string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/lists/{listId}/share-links/{token}", null, ct);

    // ── Watchers (per-user shared access to a list) ─────────────────────────────
    // Shapes verified live 2026-08-01: GET → { watchers }, POST { userId, role }
    // → 201, DELETE …/{userId}. Role defaults to "watcher".

    public async Task<List<Collaborator>> GetListWatchersAsync(string listId, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/watchers", ct);
        return json.TryGetProperty("watchers", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<Collaborator>>(JsonOptions) ?? new()
            : new();
    }

    public Task AddListWatcherAsync(string listId, string userId, string role = "watcher", CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/lists/{listId}/watchers", new { userId, role }, ct);

    public Task RemoveListWatcherAsync(string listId, string userId, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Delete, $"api/lists/{listId}/watchers/{userId}", null, ct);

    public async Task<List<UserSearchResult>> SearchListWatcherUsersAsync(string listId, string query, CancellationToken ct = default)
    {
        var json = await GetElementAsync($"api/lists/{listId}/watchers/users?q={Uri.EscapeDataString(query)}", ct);
        return json.TryGetProperty("users", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.Deserialize<List<UserSearchResult>>(JsonOptions) ?? new()
            : new();
    }
}
