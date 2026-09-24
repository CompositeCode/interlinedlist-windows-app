using System.Net;
using System.Text.Json;
using InterlinedList.Models;
using InterlinedList.Services;
using Xunit;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// One test per read endpoint the app depends on. Each case drives the *real*
/// <see cref="InterlinedApiClient"/> method the app calls, which means three
/// assertions happen at once and none of them can be forgotten:
///
///   status code   - the client throws InterlinedApiException on non-2xx
///   auth model    - the call carries only the bearer token, so a 401 means the
///                   endpoint stopped accepting it
///   required fields - the models use C# `required` properties, and
///                   System.Text.Json throws JsonException when a required
///                   member is missing from the payload
///
/// That last point is why this project compile-includes the app's own models
/// instead of re-declaring shapes: a hand-written expectation list would drift
/// from the app, which is the exact failure mode this suite exists to prevent.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ReadContractTests
{
    private readonly ContractEnvironment _env;

    public ReadContractTests(ContractEnvironment env) => _env = env;

    public static IEnumerable<object[]> ReadEndpoints() =>
        ReadProbes.All.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => new object[] { k });

    [SkippableTheory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task Endpoint_still_satisfies_the_app(string endpoint)
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);

        var probe = ReadProbes.All[endpoint];

        string observed;
        try
        {
            observed = await probe(_env);
        }
        catch (SkipException)
        {
            // Probe decided it had nothing real to address (e.g. the account has
            // no organizations). Already recorded as NotRun by the probe helper.
            throw;
        }
        catch (InterlinedApiException ex)
        {
            DriftLog.Drift(endpoint, $"HTTP {ex.StatusCode} — the app treats this endpoint as a working read. {Trim(ex.Message)}");
            throw new Xunit.Sdk.XunitException(
                $"{endpoint} returned HTTP {ex.StatusCode}. The app calls this endpoint with a bearer token and expects success. {Trim(ex.Message)}");
        }
        catch (JsonException ex)
        {
            // This is the high-value failure: the endpoint answered, but the
            // payload no longer carries a field the app's model marks `required`.
            DriftLog.Drift(endpoint, $"payload no longer deserialises into the app's model: {Trim(ex.Message)}");
            throw new Xunit.Sdk.XunitException(
                $"{endpoint} responded, but the body does not satisfy the app's wire model. " +
                $"A `required` property is missing or changed type: {Trim(ex.Message)}");
        }

        DriftLog.Ok(endpoint, observed);
    }

    private static string Trim(string s) =>
        s.Length <= 300 ? s.Replace("\r", " ").Replace("\n", " ") : s[..300].Replace("\r", " ").Replace("\n", " ") + "…";
}

/// <summary>
/// The read-endpoint registry. Keyed by "VERB path-template" so the drift report
/// and the endpoint-inventory ledger talk about endpoints in the same words.
/// </summary>
internal static class ReadProbes
{
    /// <summary>
    /// The app-settings namespace this suite reads and writes. Deliberately not
    /// a real app's key: the store is per-user per-app, so a throwaway key
    /// cannot disturb anything a companion app relies on.
    /// </summary>
    /// <summary>
    /// A transit agency slug the endpoint accepts. The `agency` parameter is
    /// REQUIRED and is absent from the OpenAPI spec entirely — recovered by
    /// probing, and its vocabulary is city slugs rather than operator names
    /// (`seattle` and `portland` were accepted; `sound-transit`, `nyc`, `bart`
    /// and ~30 others were not). Hard-coded rather than taken from
    /// TransitAgencies, which resolves a slug from coordinates and exposes no
    /// named constant.
    /// </summary>
    internal const string SeattleAgencySlug = "seattle";

    internal const string ContractAppKey = "contract-tests";

    /// <summary>
    /// Device id for the same namespace. Must satisfy the documented
    /// <c>^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$</c> — note the 8-character minimum.
    /// </summary>
    internal const string ContractDeviceId = "contract-tests-device";

    internal static readonly IReadOnlyDictionary<string, Func<ContractEnvironment, Task<string>>> All =
        new Dictionary<string, Func<ContractEnvironment, Task<string>>>(StringComparer.Ordinal)
        {
            // ── Account / identity ──────────────────────────────────────────────
            ["GET api/user"] = async e =>
            {
                var me = await e.Client.GetCurrentUserAsync();
                Assert.False(string.IsNullOrWhiteSpace(me.Id));
                Assert.False(string.IsNullOrWhiteSpace(me.Username));
                Assert.False(string.IsNullOrWhiteSpace(me.Email));
                return $"user {me.Username}, maxMessageLength={me.MaxMessageLength}";
            },

            ["GET api/user/sessions"] = async e =>
            {
                var sessions = await e.Client.GetSessionsAsync();
                // The current token must be in its own session list, or revoke-by-id
                // (the app's only way to cut off a lost device) cannot work.
                Assert.Contains(sessions, s => s.IsCurrent);
                return $"{sessions.Count} active sessions; isCurrent present";
            },

            ["GET api/user/identities"] = async e =>
            {
                var identities = await e.Client.GetLinkedIdentitiesAsync();
                Assert.All(identities, i => Assert.False(string.IsNullOrWhiteSpace(i.Provider)));
                return $"{identities.Count} linked identities: {string.Join(", ", identities.Select(i => i.Provider))}";
            },

            ["GET api/user/notification-preferences"] = async e =>
            {
                var prefs = await e.Client.GetNotificationPreferencesAsync();
                Assert.NotEmpty(prefs);
                Assert.All(prefs, p => Assert.False(string.IsNullOrWhiteSpace(p.Key)));
                return $"{prefs.Count} notifiable events";
            },

            ["GET api/user/blocks"] = async e =>
            {
                var blocked = await e.Client.GetBlockedUsersAsync();
                return $"{blocked.Count} blocked users";
            },

            ["GET api/user/mutes"] = async e =>
            {
                var muted = await e.Client.GetMutedUsersAsync();
                return $"{muted.Count} muted users";
            },

            ["GET api/user/organizations"] = async e =>
            {
                var page = await e.Client.GetMyOrganizationsAsync();
                return $"{page.Organizations.Count} organizations (pagination={(page.Pagination is null ? "absent, as the model allows" : "present")})";
            },

            // ── Added 2026-09-24 (second batch) ─────────────────────────────────

            ["GET api/folders"] = async e =>
            {
                var folders = await e.Client.GetListFoldersAsync();
                // Nesting is expressed only through parentId — the payload is flat.
                Assert.All(folders, f => Assert.False(string.IsNullOrWhiteSpace(f.Id)));
                return $"{folders.Count} list folder(s), {folders.Count(f => f.IsRoot)} at root";
            },

            ["GET api/tags/trending"] = async e =>
            {
                var tags = await e.Client.GetTrendingTagsAsync();
                Assert.All(tags, t => Assert.False(string.IsNullOrWhiteSpace(t.Tag)));
                return $"{tags.Count} trending tag(s)";
            },

            ["GET api/tags/autocomplete"] = async e =>
            {
                // An empty q is a 400, so the client short-circuits it; use a
                // single letter, which matches broadly without assuming content.
                var tags = await e.Client.AutocompleteTagsAsync("a");
                return $"{tags.Count} completion(s) for 'a'";
            },

            ["GET api/link-metadata"] = async e =>
            {
                // A stable, long-lived URL — this asserts the unfurler answers,
                // not that any particular site is up.
                var link = await e.Client.GetLinkMetadataAsync("https://example.com/");
                return link is null
                    ? "no metadata returned for example.com"
                    : $"platform={link.Platform ?? "(none)"}, title={(link.Metadata?.Title is { Length: > 0 } t ? t : "(none)")}";
            },

            ["GET api/documents/{documentId}"] = async e =>
            {
                var id = Require(e.Ids.DocumentId, "GET api/documents/{documentId}", "the account has no document to address");
                var doc = await e.Client.GetDocumentAsync(id);
                // The tree omits `content`; this is the only bulk source of a body,
                // so the editor breaks if it stops carrying one.
                Assert.False(string.IsNullOrWhiteSpace(doc.Id));
                return $"document {doc.Title}, content {(doc.Content is null ? "absent" : $"{doc.Content.Length} chars")}";
            },

            ["GET api/messages/{messageId}/metadata"] = async e =>
            {
                var id = Require(e.Ids.MessageId, "GET api/messages/{messageId}/metadata", "the feed returned no message to address");
                var links = await e.Client.GetMessageLinkMetadataAsync(id);
                // A message with no links legitimately returns an empty set.
                return $"{links.Count} stored link preview(s)";
            },

            // ── Added 2026-09-24 with the parity merge pass ─────────────────────
            // The inventory test demanded these: classifying an endpoint 'read'
            // in the manifest without a probe here is a failure, deliberately.

            ["GET api/limits"] = async e =>
            {
                var limits = await e.Client.GetLimitsAsync(forceRefresh: true);
                // The composer and every file picker size themselves off these.
                Assert.NotNull(limits.Message);
                Assert.True(limits.Message!.MaxContentLength > 0);
                Assert.NotNull(limits.Media?.Image);
                return $"image {limits.Media!.Image!.MaxBytes}B, message {limits.Message.MaxContentLength} chars";
            },

            ["GET api/ai/status"] = async e =>
            {
                var status = await e.Client.GetAiStatusAsync();
                // `providers` drives whether AI is shown at all; `quota` gates it.
                Assert.NotNull(status.Providers);
                Assert.NotNull(status.Quota);
                return $"subscriber={status.Subscriber}, providers=[{string.Join(",", status.Providers!)}], quota {status.Quota!.UsedToday}/{status.Quota.DailyLimit}";
            },

            ["GET api/user/engagement"] = async e =>
            {
                // Documented in CLAUDE.md as 401-walled for bearer clients until
                // 2026-09-15. This probe is the standing guard against that
                // regressing, since the feature quietly disappears if it does.
                var eng = await e.Client.GetEngagementAsync();
                return $"{eng.TotalDigs} digs, {eng.TotalPushes} pushes, {eng.Recent?.Count ?? 0} recent";
            },

            ["GET api/documents/tree"] = async e =>
            {
                var tree = await e.Client.GetDocumentTreeAsync();
                return $"{tree.Folders?.Count ?? 0} folders, {tree.RootDocuments?.Count ?? 0} root documents";
            },

            ["GET api/dm"] = async e =>
            {
                var page = await e.Client.GetDmFolderAsync(DmFolder.Inbox, take: 5);
                return $"inbox: {page.Items?.Count ?? 0} message(s)";
            },

            ["GET api/dm/conversations"] = async e =>
            {
                var page = await e.Client.GetDmConversationsAsync(take: 5);
                return $"{page.Items?.Count ?? 0} conversation(s)";
            },

            ["GET api/linkedin/targets"] = async e =>
            {
                // Returns 200 with an empty array whether or not LinkedIn is
                // linked, so an empty result is not a failure here.
                var targets = await e.Client.GetLinkedInTargetsAsync();
                return $"{targets.Count} LinkedIn target(s)";
            },

            ["GET api/auth/github/status"] = async e =>
            {
                var state = await e.Client.GetGitHubLinkStateAsync();
                return $"github link state: {state}";
            },

            ["GET api/github/repos"] = async e =>
            {
                // Without ?org= this is empty for an account owning no repos —
                // which is the test account. Asserting only that it answers.
                var repos = await e.Client.GetGitHubReposAsync();
                return $"{repos.Count} repo(s) without an org filter";
            },

            ["GET api/github/orgs"] = async e =>
            {
                var orgs = await e.Client.GetGitHubOrgsAsync();
                return $"{orgs.Count} org(s)";
            },

            // ── Widgets ─────────────────────────────────────────────────────────
            // These proxy third-party services, so an upstream outage must not
            // fail the suite. Transit in particular answers HTTP 200 carrying
            // {"error":"unavailable"} in the body, so status alone proves nothing.

            ["GET api/widgets/news"] = async e =>
            {
                var news = await e.Client.GetNewsWidgetAsync();
                return $"{news.Items?.Count ?? 0} headline(s)";
            },

            ["GET api/widgets/markets"] = async e =>
            {
                var markets = await e.Client.GetMarketsWidgetAsync();
                return $"{markets.Quotes?.Count ?? 0} quote(s)";
            },

            ["GET api/widgets/transit"] = async e =>
            {
                var transit = await e.Client.GetTransitAsync(SeattleAgencySlug);
                return transit.Error is { Length: > 0 }
                    ? $"agency {transit.Agency}: upstream reports \"{transit.Error}\" (HTTP 200 — not a contract failure)"
                    : $"agency {transit.Agency}: {transit.Stops?.Count ?? 0} stop(s)";
            },

            ["GET api/widgets/transit/stops"] = async e =>
            {
                var stops = await e.Client.GetTransitStopsAsync(SeattleAgencySlug);
                return stops.Error is { Length: > 0 }
                    ? $"upstream reports \"{stops.Error}\" (HTTP 200 — not a contract failure)"
                    : $"{stops.Stops?.Count ?? 0} nearby stop(s)";
            },

            ["GET api/widgets/bike-share"] = async e =>
            {
                var bikes = await e.Client.GetBikeShareAsync();
                // Two payload shapes keyed by `kind` (dockless vs docked); both
                // are valid, so only the discriminator is asserted.
                Assert.False(string.IsNullOrWhiteSpace(bikes.Kind));
                return $"kind={bikes.Kind}";
            },

            // ── Application settings ────────────────────────────────────────────
            // Read against a throwaway appKey, so these never touch a real app's
            // synced settings. 404 means "nothing stored yet" and is expected.

            ["GET api/user/app-settings/{appKey}"] = async e =>
            {
                var doc = await e.Client.GetAccountSettingsAsync(ContractAppKey);
                return doc is null
                    ? "no account document for the throwaway appKey (404, as expected)"
                    : $"version {doc.Version}, schemaVersion {doc.SchemaVersion}";
            },

            ["GET api/user/app-settings/{appKey}/devices"] = async e =>
            {
                var devices = await e.Client.GetAppDevicesAsync(ContractAppKey);
                return $"{devices.Count} registered device(s) for the throwaway appKey";
            },

            ["GET api/user/app-settings/{appKey}/bootstrap"] = async e =>
            {
                // 404 {"source":"none"} is the documented "nothing to seed"
                // answer and must be treated as normal, not an error.
                var boot = await e.Client.GetAppSettingsBootstrapAsync(ContractAppKey, ContractDeviceId);
                return $"bootstrap source: {boot.Source ?? "none"}";
            },

            // ── Messages / feed ─────────────────────────────────────────────────
            ["GET api/messages"] = async e =>
            {
                var page = await e.Client.GetMessagesAsync(limit: 5);
                Assert.NotNull(page.Pagination);
                Assert.All(page.Messages, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
                return $"{page.Messages.Count} messages, total={page.Pagination.Total}";
            },

            ["GET api/messages/{id}"] = async e =>
            {
                var id = Require(e.Ids.MessageId, "GET api/messages/{id}", "the feed returned no message to address");
                var message = await e.Client.GetMessageAsync(id);
                Assert.Equal(id, message.Id);
                return $"message {message.Id}, digCount={message.DigCount}";
            },

            ["GET api/messages/{id}/replies"] = async e =>
            {
                var id = Require(e.Ids.MessageId, "GET api/messages/{id}/replies", "the feed returned no message to address");
                var replies = await e.Client.GetRepliesAsync(id);
                return $"{replies.Count} replies";
            },

            ["GET api/messages/search"] = async e =>
            {
                var page = await e.Client.SearchMessagesAsync("the", limit: 5);
                Assert.NotNull(page.Pagination);
                return $"{page.Messages.Count} hits for 'the'";
            },

            ["GET api/messages/scheduled"] = async e =>
            {
                var scheduled = await e.Client.GetScheduledMessagesAsync();
                return $"{scheduled.Count} scheduled messages";
            },

            // ── Notifications ───────────────────────────────────────────────────
            ["GET api/notifications"] = async e =>
            {
                var page = await e.Client.GetNotificationsAsync(limit: 5);
                Assert.NotNull(page.Items);
                // The tray wraps its array under "items", not "notifications" —
                // an easy thing to get wrong when re-reading the API by eye.
                return $"{page.Items.Count} tray items, unreadCount={page.UnreadCount}";
            },

            // ── Lists ───────────────────────────────────────────────────────────
            ["GET api/lists"] = async e =>
            {
                var page = await e.Client.GetListsAsync(limit: 5);
                Assert.NotNull(page.Pagination);
                return $"{page.Lists.Count} lists, total={page.Pagination.Total}";
            },

            ["GET api/lists/{listId}"] = async e =>
            {
                var id = Require(e.Ids.ListId, "GET api/lists/{listId}", "the account has no list to address");
                var list = await e.Client.GetListAsync(id);
                Assert.Equal(id, list.Id);
                // Still wrapped in a "data" envelope, unlike the collection read.
                return $"list '{list.Title}' via the data envelope";
            },

            ["GET api/lists/{listId}/data"] = async e =>
            {
                var id = Require(e.Ids.ListId, "GET api/lists/{listId}/data", "the account has no list to address");
                return await ProbeListDataAsync(e, id);
            },

            ["GET api/lists/{listId}/share-links"] = async e =>
            {
                var id = Require(e.Ids.ListId, "GET api/lists/{listId}/share-links", "the account has no list to address");
                var links = await e.Client.GetListShareLinksAsync(id);
                return $"{links.Count} share links";
            },

            ["GET api/lists/{listId}/watchers"] = async e =>
            {
                var id = Require(e.Ids.ListId, "GET api/lists/{listId}/watchers", "the account has no list to address");
                var watchers = await e.Client.GetListWatchersAsync(id);
                return $"{watchers.Count} watchers";
            },

            ["GET api/lists/{listId}/watchers/users"] = async e =>
            {
                var id = Require(e.Ids.ListId, "GET api/lists/{listId}/watchers/users", "the account has no list to address");
                var users = await e.Client.SearchListWatcherUsersAsync(id, "a");
                return $"{users.Count} candidate users for 'a'";
            },

            ["GET api/lists/watching"] = async e =>
            {
                var watched = await e.Client.GetWatchingListsAsync();
                return $"{watched.Count} watched lists";
            },

            ["GET api/lists/search"] = async e =>
            {
                var hits = await e.Client.SearchListsAsync("a", limit: 5);
                return $"{hits.Count} list hits for 'a'";
            },

            // ── Documents ───────────────────────────────────────────────────────
            ["GET api/documents"] = async e =>
            {
                var page = await e.Client.GetRootDocumentsAsync();
                Assert.All(page.Documents, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
                return $"{page.Documents.Count} root documents";
            },

            ["GET api/documents/{id}"] = async e =>
            {
                var id = Require(e.Ids.DocumentId, "GET api/documents/{id}", "the account has no document to address");
                var doc = await e.Client.GetDocumentAsync(id);
                Assert.Equal(id, doc.Id);
                return $"document '{doc.Title}' via the document envelope";
            },

            ["GET api/documents/{documentId}/share-links"] = async e =>
            {
                var id = Require(e.Ids.DocumentId, "GET api/documents/{documentId}/share-links", "the account has no document to address");
                var links = await e.Client.GetDocumentShareLinksAsync(id);
                return $"{links.Count} share links";
            },

            ["GET api/documents/{documentId}/collaborators"] = async e =>
            {
                var id = Require(e.Ids.DocumentId, "GET api/documents/{documentId}/collaborators", "the account has no document to address");
                var collaborators = await e.Client.GetDocumentCollaboratorsAsync(id);
                return $"{collaborators.Count} collaborators";
            },

            ["GET api/documents/{documentId}/collaborators/users"] = async e =>
            {
                var id = Require(e.Ids.DocumentId, "GET api/documents/{documentId}/collaborators/users", "the account has no document to address");
                var users = await e.Client.SearchCollaboratorUsersAsync(id, "a");
                return $"{users.Count} candidate users for 'a'";
            },

            ["GET api/documents/folders"] = async e =>
            {
                var page = await e.Client.GetDocumentFoldersAsync();
                // Each folder carries its documents inline; the sync utility's
                // folder-tree materialisation depends on that staying true.
                Assert.All(page.Folders, f => Assert.NotNull(f.Documents));
                return $"{page.Folders.Count} folders, documents embedded inline";
            },

            ["GET api/documents/templates"] = async e =>
            {
                var response = await e.Client.GetDocumentTemplatesAsync();
                return $"{response.Templates.Count} templates, folderId={response.TemplatesFolderId ?? "(none)"}";
            },

            ["GET api/documents/search"] = async e =>
            {
                var hits = await e.Client.SearchDocumentsAsync("a", limit: 5);
                return $"{hits.Count} document hits for 'a'";
            },

            // ── Organizations ───────────────────────────────────────────────────
            ["GET api/organizations"] = async e =>
            {
                var page = await e.Client.GetAllOrganizationsAsync(limit: 5);
                return $"{page.Organizations.Count} organizations";
            },

            ["GET api/organizations/{id}"] = async e =>
            {
                var id = Require(e.Ids.OrganizationId, "GET api/organizations/{id}", "no organization is visible to this account");
                var org = await e.Client.GetOrganizationAsync(id);
                Assert.Equal(id, org.Id);
                return $"organization '{org.Name}', memberCount={org.MemberCount}";
            },

            ["GET api/organizations/{orgId}/members"] = async e =>
            {
                var id = Require(e.Ids.OrganizationId, "GET api/organizations/{orgId}/members", "no organization is visible to this account");
                var members = await e.Client.GetOrgMembersAsync(id);
                // CLAUDE.md once recorded this as 401-walled for bearer tokens.
                // It is not; keep asserting it so the correction stays true.
                return $"{members.Count} members readable with the bearer token";
            },

            // ── Direct messages (reads only) ────────────────────────────────────
            ["GET api/dm/recipients"] = async e =>
            {
                var recipients = await e.Client.GetDmRecipientsAsync();
                return $"{recipients.Count} DM recipients";
            },

            ["GET api/dm/thread/{username}"] = async e =>
            {
                var who = Require(e.Ids.DmPartnerUsername, "GET api/dm/thread/{username}", "no DM partner available");
                var thread = await e.Client.GetDmThreadAsync(who);
                Assert.NotNull(thread.Items);
                return $"thread with {who}: {thread.Items.Count} items, isMutual={thread.IsMutual}";
            },

            ["GET api/dm/thread/{username}/updates"] = async e =>
            {
                var who = Require(e.Ids.DmPartnerUsername, "GET api/dm/thread/{username}/updates", "no DM partner available");
                var updates = await e.Client.GetDmThreadUpdatesAsync(who);
                return $"{updates.Count} thread updates";
            },

            ["GET api/dm/unread-count"] = async e =>
            {
                var count = await e.Client.GetDmUnreadCountAsync();
                Assert.True(count >= 0);
                return $"unread count={count}";
            },

            // ── Social graph (reads only) ───────────────────────────────────────
            ["GET api/follow/requests"] = async e =>
            {
                var requests = await e.Client.GetFollowRequestsAsync();
                return $"{requests.Count} pending follow requests";
            },

            ["GET api/follow/{userId}/status"] = async e =>
            {
                var id = Require(e.Ids.UserId, "GET api/follow/{userId}/status", "own user id unknown");
                var status = await e.Client.GetFollowStatusAsync(id);
                return $"isFollowing={status.IsFollowing}, isPending={status.IsPending}, status={status.Status ?? "(null)"}";
            },

            ["GET api/follow/{userId}/counts"] = async e =>
            {
                var id = Require(e.Ids.UserId, "GET api/follow/{userId}/counts", "own user id unknown");
                var counts = await e.Client.GetFollowCountsAsync(id);
                return $"followers={counts.Followers}, following={counts.Following}, pending={counts.PendingRequests}";
            },

            ["GET api/follow/{userId}/followers"] = async e =>
            {
                var id = Require(e.Ids.UserId, "GET api/follow/{userId}/followers", "own user id unknown");
                var followers = await e.Client.GetFollowersAsync(id);
                return $"{followers.Count} followers";
            },

            ["GET api/follow/{userId}/following"] = async e =>
            {
                var id = Require(e.Ids.UserId, "GET api/follow/{userId}/following", "own user id unknown");
                var following = await e.Client.GetFollowingAsync(id);
                return $"{following.Count} following";
            },

            // GetMutualAsync pulls a "mutual" array out of this payload and
            // tolerates its absence by returning an empty list — so the typed
            // client CANNOT report drift here; a broken endpoint looks like
            // "no mutual follows". Assert the raw shape instead.
            ["GET api/follow/{userId}/mutual"] = async e =>
            {
                var id = Require(e.Ids.UserId, "GET api/follow/{userId}/mutual", "own user id unknown");
                var (status, body) = await e.RawGetAsync($"api/follow/{id}/mutual");
                Assert.Equal(System.Net.HttpStatusCode.OK, status);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var hasArray = root.TryGetProperty("mutual", out var arr) && arr.ValueKind == JsonValueKind.Array;

                if (!hasArray)
                {
                    // The app/API mismatch this suite was built to surface. Fixing
                    // it means changing InterlinedList/Services, which this PR must
                    // not touch — so it is reported as a KNOWN mismatch rather than
                    // a failure, and the assertions below pin the shape that IS
                    // returned. If the API later grows the "mutual" array, this
                    // probe stops matching and the test fails, which is the prompt
                    // to fix the client and delete this branch.
                    var keys = string.Join(", ", root.EnumerateObject().Select(p => p.Name));
                    Assert.True(root.TryGetProperty("mutualFollowers", out _), "expected a mutualFollowers count");
                    Assert.True(root.TryGetProperty("mutualFollowing", out _), "expected a mutualFollowing count");

                    DriftLog.Known(
                        "GET api/follow/{userId}/mutual",
                        $"returns {{ {keys} }} — counts, not a user array. InterlinedApiClient.GetMutualAsync reads a \"mutual\" array " +
                        "and tolerates its absence, so it always returns an empty list: the mutual-follows feature is silently dead in the app. " +
                        "The endpoint is healthy; the client's expectation is wrong. Fix belongs in InterlinedList/Services.");

                    return $"counts only ({keys}) — client expects an array; tracked as a known mismatch";
                }

                return $"{arr.GetArrayLength()} mutual users";
            },

            // ── People ──────────────────────────────────────────────────────────
            ["GET api/users/search"] = async e =>
            {
                var page = await e.Client.SearchUsersAsync("a");
                Assert.All(page.Users, u => Assert.False(string.IsNullOrWhiteSpace(u.Username)));
                return $"{page.Users.Count} of {page.Total} users match 'a'";
            },

            ["GET api/users/{username}"] = async e =>
            {
                var who = Require(e.Ids.Username, "GET api/users/{username}", "own username unknown");
                var profile = await e.Client.GetProfileAsync(who);
                Assert.Equal(who, profile.Username);
                return $"profile {profile.Username}, followers={profile.FollowerCount}";
            },

            ["GET api/users/{username}/lists"] = async e =>
            {
                var who = Require(e.Ids.Username, "GET api/users/{username}/lists", "own username unknown");
                var page = await e.Client.GetUserListsAsync(who);
                return $"{page.Lists.Count} public lists (PLURAL 'users' path)";
            },

            ["GET api/user/{username}/messages"] = async e =>
            {
                var who = Require(e.Ids.Username, "GET api/user/{username}/messages", "own username unknown");
                var page = await e.Client.GetUserMessagesAsync(who, limit: 5);
                // Deliberate asymmetry in the API: a user's MESSAGES live under
                // the singular "/api/user/{username}/messages" while the profile
                // and lists live under the plural "/api/users/{username}".
                // Normalising the client to one spelling yields a 404.
                return $"{page.Messages.Count} messages (SINGULAR 'user' path — the plural spelling 404s)";
            },

            ["GET api/users/{username}/block"] = async e =>
            {
                var who = Require(e.Ids.Username, "GET api/users/{username}/block", "own username unknown");
                var blocking = await e.Client.IsBlockingAsync(who);
                return $"blocked={blocking}";
            },

            ["GET api/users/{username}/mute"] = async e =>
            {
                var who = Require(e.Ids.Username, "GET api/users/{username}/mute", "own username unknown");
                var muting = await e.Client.IsMutingAsync(who);
                return $"muted={muting}";
            },

            // ── CSV exports (text/csv, not JSON) ────────────────────────────────
            ["GET api/exports/messages"] = async e => await Csv(() => e.Client.ExportMessagesCsvAsync(), "ID,Content"),
            ["GET api/exports/lists"] = async e => await Csv(() => e.Client.ExportListsCsvAsync(), "ID,Title"),
            ["GET api/exports/list-data-rows"] = async e => await Csv(() => e.Client.ExportListDataRowsCsvAsync(), "ID,List ID"),
            ["GET api/exports/follows"] = async e => await Csv(() => e.Client.ExportFollowsCsvAsync(), "ID,Relationship Type"),
        };

    /// <summary>
    /// List rows, asserted against the raw payload first and the typed model
    /// second — because the two disagree today.
    ///
    /// GET /api/lists/{id}/data returns rows WITHOUT a "listId" property, while
    /// InterlinedList.Models.ListDataRow marks ListId as `required`. So
    /// GetListDataAsync throws JsonException for any list that actually has
    /// rows. A read-only probe never caught this: the test account's only list
    /// was empty, so the row shape was never exercised. It takes creating a row
    /// to see it, which is exactly why the write lifecycle below exists.
    ///
    /// Shared by the read probe and the write lifecycle so both describe the
    /// mismatch identically.
    /// </summary>
    internal static async Task<string> ProbeListDataAsync(ContractEnvironment e, string listId)
    {
        var (status, body) = await e.RawGetAsync($"api/lists/{listId}/data?limit=50&offset=0");
        Assert.Equal(HttpStatusCode.OK, status);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("pagination", out _), "list data must still carry a pagination envelope");
        var rows = doc.RootElement.GetProperty("rows");

        var rowsMissingListId = rows.EnumerateArray().Count(r => !r.TryGetProperty("listId", out _));
        if (rowsMissingListId > 0)
        {
            var keys = string.Join(", ", rows.EnumerateArray().First().EnumerateObject().Select(p => p.Name));
            DriftLog.Known(
                "GET api/lists/{listId}/data",
                $"rows omit \"listId\" (row keys: {keys}), but InterlinedList.Models.ListDataRow marks ListId as `required`, " +
                "so GetListDataAsync throws JsonException for any non-empty list — the Lists data view cannot load rows. " +
                "POST to the same path DOES return listId, so the field exists in the write envelope only. " +
                "Fix belongs in InterlinedList/Models (make ListId nullable or drop it).");
        }

        try
        {
            var page = await e.Client.GetListDataAsync(listId, limit: 50);
            return $"{page.Rows.Count} rows, typed deserialisation OK";
        }
        catch (JsonException) when (rowsMissingListId > 0)
        {
            return $"{rows.GetArrayLength()} rows on the wire; the app's typed model rejects them (known mismatch)";
        }
    }

    /// <summary>
    /// An id-shaped endpoint with no real object to address is honestly "not
    /// covered this run" — recorded in the report and skipped, never quietly
    /// passed. A green test that addressed nothing is worse than no test.
    /// </summary>
    private static string Require(string? value, string endpoint, string why)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            DriftLog.NotRun(endpoint, $"not exercised: {why}");
            Skip.If(true, $"{endpoint}: {why}");
        }

        return value!;
    }

    private static async Task<string> Csv(Func<Task<string>> call, string expectedHeaderPrefix)
    {
        var csv = await call();
        Assert.False(string.IsNullOrWhiteSpace(csv));
        Assert.StartsWith(expectedHeaderPrefix, csv, StringComparison.Ordinal);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return $"CSV, header '{expectedHeaderPrefix}…', {lines} lines";
    }
}
