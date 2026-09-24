using System.Text.Json;
using InterlinedList.Models;
using InterlinedList.Services;
using Xunit;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// The write endpoints, exercised the only way a live shared API permits:
/// against objects this suite creates and then deletes, in the same test, in a
/// finally block.
///
/// What is deliberately NOT here is the point. No posted messages, no DMs, no
/// digs, no organizations, no linked-identity changes, no follows, no blocks, no
/// avatar changes, no notification state. CLAUDE.md records that
/// CreateOrganizationAsync and RemoveIdentityAsync were never exercised live on
/// purpose; this suite keeps that promise instead of quietly breaking it for
/// coverage's sake. endpoints.manifest.tsv lists every one of those with its
/// reason, and EndpointInventoryTests fails if a new one appears unclassified.
///
/// These tests also prove the repo's read-after-write discipline still works:
/// several client mutators (AddListRowAsync, CreateDocumentFolderAsync, …)
/// deliberately do not parse their response body, so the only way to know a
/// write landed is to re-fetch it. Each case below does exactly that.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class WriteContractTests
{
    private readonly ContractEnvironment _env;

    public WriteContractTests(ContractEnvironment env) => _env = env;

    [SkippableFact]
    public async Task Document_create_read_update_delete_roundtrip()
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);

        var title = ContractEnvironment.Throwaway("doc");
        string? id = null;

        try
        {
            var created = await _env.Client.CreateDocumentAsync(title, "created by the contract suite");
            id = created.Id;
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal(title, created.Title);
            DriftLog.Ok("POST api/documents", "returns the new document under a \"document\" envelope");

            var fetched = await _env.Client.GetDocumentAsync(id);
            Assert.Equal(title, fetched.Title);
            Assert.Equal("created by the contract suite", fetched.Content);

            // PATCH's response body is deliberately unparsed by the client, so
            // the assertion has to be a re-read.
            await _env.Client.UpdateDocumentAsync(id, title, "edited by the contract suite");
            var updated = await _env.Client.GetDocumentAsync(id);
            Assert.Equal("edited by the contract suite", updated.Content);
            DriftLog.Ok("PATCH api/documents/{id}", "edit confirmed by re-reading the document");

            await ShareLinkRoundtripAsync(
                "POST api/documents/{documentId}/share-links",
                "DELETE api/documents/{documentId}/share-links/{token}",
                () => _env.Client.CreateDocumentShareLinkAsync(id!),
                () => _env.Client.GetDocumentShareLinksAsync(id!),
                token => _env.Client.DeleteDocumentShareLinkAsync(id!, token));
        }
        finally
        {
            if (id is not null)
            {
                await _env.Client.DeleteDocumentAsync(id);
                DriftLog.Ok("DELETE api/documents/{id}", "throwaway document removed");
                await AssertGoneAsync(
                    "DELETE api/documents/{id}",
                    async () => (await _env.Client.GetRootDocumentsAsync()).Documents.Any(d => d.Id == id));
            }
        }
    }

    [SkippableFact]
    public async Task Document_folder_create_rename_populate_delete_roundtrip()
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);

        var name = ContractEnvironment.Throwaway("folder");
        var renamed = name + "-renamed";
        string? folderId = null;

        try
        {
            // CreateDocumentFolderAsync returns void — the folder id has to come
            // from a re-fetch. That is the contract being asserted.
            await _env.Client.CreateDocumentFolderAsync(name);
            folderId = await FindFolderIdAsync(name);
            Assert.NotNull(folderId);
            DriftLog.Ok("POST api/documents/folders", "folder created; id recovered by re-reading the folder list");

            await _env.Client.RenameDocumentFolderAsync(folderId!, renamed);
            Assert.Equal(folderId, await FindFolderIdAsync(renamed));
            DriftLog.Ok("PUT api/documents/folders/{id}", "rename confirmed by re-reading the folder list");

            var docTitle = ContractEnvironment.Throwaway("foldered-doc");
            await _env.Client.CreateDocumentInFolderAsync(folderId!, docTitle, "in a folder");

            var folders = await _env.Client.GetDocumentFoldersAsync();
            var folder = folders.Folders.Single(f => f.Id == folderId);
            Assert.Contains(folder.Documents, d => d.Title == docTitle);
            DriftLog.Ok(
                "POST api/documents/folders/{folderId}/documents",
                "new document appears inline in the folder's embedded documents array");

            // Empty the folder before removing it — cascade behaviour is not
            // something this suite should discover the hard way on a live system.
            foreach (var entry in folder.Documents)
                await _env.Client.DeleteDocumentAsync(entry.Id);
        }
        finally
        {
            if (folderId is not null)
            {
                await _env.Client.DeleteDocumentFolderAsync(folderId);
                DriftLog.Ok("DELETE api/documents/folders/{id}", "throwaway folder removed");
                Assert.Null(await FindFolderIdAsync(renamed));
            }
        }
    }

    [SkippableFact]
    public async Task List_create_update_row_lifecycle_delete_roundtrip()
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);

        var title = ContractEnvironment.Throwaway("list");
        string? listId = null;

        try
        {
            var created = await _env.Client.CreateListAsync(title, "created by the contract suite");
            listId = created.Id;
            Assert.False(string.IsNullOrWhiteSpace(listId));
            DriftLog.Ok("POST api/lists", "returns the new list under a \"data\" envelope");

            await _env.Client.UpdateListAsync(listId, title + "-renamed", "edited by the contract suite");
            var afterUpdate = await _env.Client.GetListAsync(listId);
            Assert.Equal(title + "-renamed", afterUpdate.Title);
            DriftLog.Ok("PUT api/lists/{listId}", "edit confirmed by re-reading the list");

            // Schema-less freeform rows: the supported path per CLAUDE.md (the
            // schema DSL is not implemented). Confirm that is still true.
            await _env.Client.AddListRowAsync(listId, new Dictionary<string, object?>
            {
                ["probe"] = "contract-suite",
                ["number"] = 1,
            });

            // Row verification goes through the raw payload, not GetListDataAsync.
            // The typed model currently cannot deserialize a populated list (rows
            // carry no "listId" though ListDataRow requires it) — see
            // ReadProbes.ProbeListDataAsync. Asserting on raw JSON keeps this
            // lifecycle able to test the write endpoints despite that, instead of
            // the whole thing going red on someone else's bug.
            await ReadProbes.ProbeListDataAsync(_env, listId);

            var rowId = await SingleRowIdAsync(listId, expectedProbe: "contract-suite");
            DriftLog.Ok("POST api/lists/{listId}/data", "schema-less row accepted; confirmed by re-reading rows");

            await _env.Client.UpdateListRowAsync(listId, rowId, new Dictionary<string, object?>
            {
                ["probe"] = "contract-suite-edited",
                ["number"] = 2,
            });

            await SingleRowIdAsync(listId, expectedProbe: "contract-suite-edited");
            DriftLog.Ok("PUT api/lists/{listId}/data/{rowId}", "row edit confirmed by re-reading rows");

            await ShareLinkRoundtripAsync(
                "POST api/lists/{listId}/share-links",
                "DELETE api/lists/{listId}/share-links/{token}",
                () => _env.Client.CreateListShareLinkAsync(listId!),
                () => _env.Client.GetListShareLinksAsync(listId!),
                token => _env.Client.DeleteListShareLinkAsync(listId!, token));

            await _env.Client.DeleteListRowAsync(listId, rowId);
            Assert.Equal(0, await RowCountAsync(listId));
            DriftLog.Ok("DELETE api/lists/{listId}/data/{rowId}", "row removal confirmed by re-reading rows");
        }
        finally
        {
            if (listId is not null)
            {
                await _env.Client.DeleteListAsync(listId);
                DriftLog.Ok("DELETE api/lists/{listId}", "throwaway list removed");
                await AssertGoneAsync(
                    "DELETE api/lists/{listId}",
                    async () => (await _env.Client.GetListsAsync(limit: 100)).Lists.Any(l => l.Id == listId));
            }
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Share links behave identically for documents and lists, so the roundtrip
    /// is shared. Endpoint labels are passed in verbatim so the drift report
    /// names them exactly as endpoints.manifest.tsv does.
    /// </summary>
    private static async Task ShareLinkRoundtripAsync(
        string createEndpoint,
        string deleteEndpoint,
        Func<Task<ShareLink>> create,
        Func<Task<List<ShareLink>>> list,
        Func<string, Task> revoke)
    {
        var link = await create();
        Assert.False(string.IsNullOrWhiteSpace(link.Token));
        DriftLog.Ok(createEndpoint, "returns the new link with a token");

        var links = await list();
        Assert.Contains(links, l => l.Token == link.Token);

        await revoke(link.Token);
        var afterRevoke = await list();
        Assert.DoesNotContain(afterRevoke, l => l.Token == link.Token && l.IsActive);
        DriftLog.Ok(deleteEndpoint, "revoked link no longer listed as active");
    }

    /// <summary>
    /// The throwaway list's single row, read from the raw payload, asserting the
    /// two fields the app genuinely needs off a row: its id (to edit/delete it)
    /// and its freeform rowData.
    /// </summary>
    private async Task<string> SingleRowIdAsync(string listId, string expectedProbe)
    {
        var (status, body) = await _env.RawGetAsync($"api/lists/{listId}/data?limit=50&offset=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, status);

        using var doc = JsonDocument.Parse(body);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        var row = Assert.Single(rows);

        Assert.True(row.TryGetProperty("id", out var id), "a row must carry an id, or it cannot be edited or deleted");
        Assert.Equal(expectedProbe, row.GetProperty("rowData").GetProperty("probe").GetString());

        return id.GetString()!;
    }

    private async Task<int> RowCountAsync(string listId)
    {
        var (_, body) = await _env.RawGetAsync($"api/lists/{listId}/data?limit=50&offset=0");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("rows").GetArrayLength();
    }

    private async Task<string?> FindFolderIdAsync(string name)
    {
        var folders = await _env.Client.GetDocumentFoldersAsync();
        return folders.Folders.FirstOrDefault(f => f.Name == name)?.Id;
    }

    /// <summary>
    /// Deletes may be soft on this API, so "gone" is asserted as "no longer
    /// listed" rather than "the id 404s" — and a still-listed object is reported
    /// as drift rather than silently tolerated.
    /// </summary>
    private static async Task AssertGoneAsync(string endpoint, Func<Task<bool>> stillListed)
    {
        try
        {
            if (await stillListed())
                DriftLog.Drift(endpoint, "the object was still listed after a successful DELETE — deletes may have become soft/deferred");
        }
        catch (InterlinedApiException ex)
        {
            DriftLog.NotRun(endpoint, $"could not confirm removal: HTTP {ex.StatusCode}");
        }
    }
}
