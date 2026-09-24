using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using InterlinedList.Models;
using InterlinedList.Services;
using Xunit;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// One sign-in, one token, one set of discovered object ids, shared by every
/// contract test in the run — then cleaned up.
///
/// Three things here are load-bearing, all of them lessons the repo already paid
/// for:
///
/// 1. ENV-GATED, NOT ENV-REQUIRED. With no credentials <see cref="IsConfigured"/>
///    is false and every test calls Skip.IfNot on it. The suite skips and the
///    build passes. Throwing here instead would turn "no secrets configured" into
///    a red build on every fork and PR, which is exactly the friction that gets
///    a contract suite deleted.
///
/// 2. ONE TOKEN PER RUN, AND WE REVOKE IT. Every call to POST /api/auth/sync-token
///    mints a *standing* credential that never expires and shows up in
///    GET /api/user/sessions forever. When this suite was written the test
///    account had 1,284 live sessions, 1,250 of them labelled "CLI" — a pile of
///    permanent credentials left behind by one-off probes. So: sign in exactly
///    once, and revoke our own session on the way out.
///
/// 3. BASE URL NORMALISATION. INTERLINEDLIST_API_BASE_URL ships with a trailing
///    slash. Concatenating "/api/user" onto it yields a double slash, and the
///    server answers "https://interlinedlist.com//api/user" with a 308 redirect
///    rather than the payload — a probe that looks like a broken endpoint but is
///    really a broken URL. <see cref="Normalize"/> makes that unrepresentable.
/// </summary>
public sealed class ContractEnvironment : IAsyncLifetime
{
    /// <summary>Everything this suite creates is named with this prefix so a leaked object is identifiable and sweepable.</summary>
    public const string ThrowawayPrefix = "il-contract-test-";

    /// <summary>Device label prefix for tokens this suite mints, so old ones can be found and revoked.</summary>
    public const string TokenLabelPrefix = "contract-tests-";

    private HttpClient? _http;
    private string? _ownSessionId;

    public bool IsConfigured { get; private set; }

    /// <summary>Human-readable reason shown on skipped tests when credentials are absent.</summary>
    public string SkipReason { get; private set; } =
        "INTERLINEDLIST_EMAIL / INTERLINEDLIST_PASSWORD are not set, so the live contract suite is skipped. " +
        "Set them in .env (see .env.example) or as repository secrets.";

    public Uri BaseUri { get; private set; } = new(ApiConfig.BaseUrl);

    public InterlinedApiClient Client { get; private set; } = null!;

    public string Token { get; private set; } = string.Empty;

    /// <summary>Real ids discovered from cheap reads, so id-shaped endpoints can be exercised.</summary>
    public DiscoveredIds Ids { get; } = new();

    public sealed class DiscoveredIds
    {
        public string? Username { get; set; }
        public string? UserId { get; set; }
        public string? MessageId { get; set; }
        public string? ListId { get; set; }
        public string? DocumentId { get; set; }
        public string? OrganizationId { get; set; }
        public string? DmPartnerUsername { get; set; }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var settings = EnvFile.Resolve();
        BaseUri = Normalize(settings.BaseUrl ?? ApiConfig.BaseUrl);

        if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
        {
            IsConfigured = false;
            if (EnvFile.Disabled)
                SkipReason = "INTERLINEDLIST_CONTRACT_TESTS=off, so the live contract suite is disabled.";
            DriftLog.NotRun("(entire suite)", SkipReason);
            return;
        }

        _http = new HttpClient { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(60) };
        Client = new InterlinedApiClient(_http);

        // The sign-in call is itself a contract assertion: POST /api/auth/sync-token
        // must still answer with { token }. RequestSyncTokenAsync throws if it doesn't.
        var label = $"{TokenLabelPrefix}{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}";
        Token = await Client.RequestSyncTokenAsync(settings.Email!, settings.Password!, label);
        Client.AccessToken = Token;
        IsConfigured = true;
        DriftLog.Ok("POST api/auth/sync-token", "returned a bearer token");

        await DiscoverAsync();
    }

    /// <summary>
    /// Cheap reads that give the id-shaped endpoints something real to address.
    /// Failures are recorded, not thrown: a discovery miss should show up as
    /// "not covered this run" in the drift report, not as a cascade of errors
    /// that hides which endpoint actually broke.
    /// </summary>
    private async Task DiscoverAsync()
    {
        await Try("GET api/user", async () =>
        {
            var me = await Client.GetCurrentUserAsync();
            Ids.Username = me.Username;
            Ids.UserId = me.Id;
        });

        await Try("GET api/messages", async () =>
        {
            var page = await Client.GetMessagesAsync(limit: 5);
            Ids.MessageId = page.Messages.FirstOrDefault()?.Id;
        });

        await Try("GET api/lists", async () =>
        {
            var page = await Client.GetListsAsync(limit: 5);
            Ids.ListId = page.Lists.FirstOrDefault()?.Id;
        });

        await Try("GET api/documents", async () =>
        {
            var page = await Client.GetRootDocumentsAsync();
            Ids.DocumentId = page.Documents.FirstOrDefault()?.Id;
        });

        await Try("GET api/organizations", async () =>
        {
            var page = await Client.GetAllOrganizationsAsync(limit: 5);
            Ids.OrganizationId = page.Organizations.FirstOrDefault()?.Id;
        });

        await Try("GET api/dm/recipients", async () =>
        {
            var recipients = await Client.GetDmRecipientsAsync();
            Ids.DmPartnerUsername = recipients.FirstOrDefault()?.Username ?? Ids.Username;
        });

        // Our own session id, so teardown revokes this run's token and nothing else.
        await Try("GET api/user/sessions", async () =>
        {
            var sessions = await Client.GetSessionsAsync();
            _ownSessionId = sessions.FirstOrDefault(s => s.IsCurrent)?.Id;
        });

        static async Task Try(string what, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                DriftLog.NotRun($"discovery via {what}", ex.Message);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (IsConfigured)
        {
            await SweepThrowawaysAsync();
            await RevokeStaleContractTokensAsync();
            // Last, because success here invalidates the token everything else uses.
            await TryRevokeOwnTokenAsync();
        }

        DriftLog.Publish();
        _http?.Dispose();
    }

    /// <summary>
    /// Belt-and-braces cleanup: each write test deletes what it made in a finally
    /// block, but a crashed run can still leak. Anything titled with
    /// <see cref="ThrowawayPrefix"/> is ours by construction, so remove it.
    /// </summary>
    private async Task SweepThrowawaysAsync()
    {
        try
        {
            var docs = await Client.GetRootDocumentsAsync();
            foreach (var d in docs.Documents.Where(d => d.Title.StartsWith(ThrowawayPrefix, StringComparison.Ordinal)))
                await Swallow(() => Client.DeleteDocumentAsync(d.Id), $"sweep document {d.Id}");

            var folders = await Client.GetDocumentFoldersAsync();
            foreach (var f in folders.Folders.Where(f => f.Name.StartsWith(ThrowawayPrefix, StringComparison.Ordinal)))
            {
                foreach (var entry in f.Documents)
                    await Swallow(() => Client.DeleteDocumentAsync(entry.Id), $"sweep foldered document {entry.Id}");
                await Swallow(() => Client.DeleteDocumentFolderAsync(f.Id), $"sweep folder {f.Id}");
            }

            var lists = await Client.GetListsAsync(limit: 100);
            foreach (var l in lists.Lists.Where(l => l.Title.StartsWith(ThrowawayPrefix, StringComparison.Ordinal)))
                await Swallow(() => Client.DeleteListAsync(l.Id), $"sweep list {l.Id}");
        }
        catch (Exception ex)
        {
            DriftLog.NotRun("throwaway sweep", ex.Message);
        }
    }

    /// <summary>
    /// Token hygiene, given a constraint the API actually enforces: DELETE
    /// /api/user/sessions/{id} refuses the caller's OWN session with
    /// "cannot_revoke_current_session" (confirmed live). A run therefore cannot
    /// delete the credential it is authenticating with.
    ///
    /// So each run revokes its PREDECESSORS instead — every session labelled
    /// "contract-tests-*" that is not the current one. Steady state is exactly
    /// one live contract-test token rather than one more per run forever, which
    /// is how the account reached 1,284 sessions before this existed.
    /// </summary>
    private async Task RevokeStaleContractTokensAsync()
    {
        const string endpoint = "DELETE api/user/sessions/{id}";

        try
        {
            var sessions = await Client.GetSessionsAsync();
            var stale = sessions
                .Where(s => !s.IsCurrent && (s.DeviceLabel ?? string.Empty).StartsWith(TokenLabelPrefix, StringComparison.Ordinal))
                .ToList();

            var revoked = 0;
            foreach (var session in stale)
            {
                try
                {
                    await Client.RevokeSessionAsync(session.Id);
                    revoked++;
                }
                catch (Exception ex)
                {
                    DriftLog.NotRun(endpoint, $"could not revoke stale session {session.Id}: {ex.Message}");
                }
            }

            DriftLog.Ok(
                endpoint,
                $"revoked {revoked} stale contract-test token(s). This run's own token is left in place because the API " +
                "rejects self-revocation (cannot_revoke_current_session); the next run cleans it up.");
        }
        catch (Exception ex)
        {
            DriftLog.NotRun(endpoint, $"stale-token sweep failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempted as the very last act of the run, so that if the API ever starts
    /// allowing self-revocation this suite leaves nothing behind at all — and if
    /// it keeps refusing, the refusal is recorded rather than assumed. Running it
    /// last is deliberate: a successful self-revoke invalidates the token every
    /// other test depends on.
    /// </summary>
    private async Task TryRevokeOwnTokenAsync()
    {
        if (_ownSessionId is null)
        {
            DriftLog.NotRun("DELETE api/user/sessions/{id} (self)", "this run's own session id was never discovered");
            return;
        }

        try
        {
            await Client.RevokeSessionAsync(_ownSessionId);
            DriftLog.Ok(
                "DELETE api/user/sessions/{id} (self)",
                "self-revocation is now ALLOWED — this run left no token behind. The stale-token sweep can be simplified.");
        }
        catch (InterlinedApiException ex) when (ex.Message.Contains("cannot_revoke_current_session", StringComparison.OrdinalIgnoreCase))
        {
            DriftLog.Ok(
                "DELETE api/user/sessions/{id} (self)",
                "refused with cannot_revoke_current_session, as expected — a token cannot delete itself, which is why each run sweeps its predecessors instead");
        }
        catch (Exception ex)
        {
            DriftLog.NotRun("DELETE api/user/sessions/{id} (self)", $"unexpected response: {ex.Message}");
        }
    }

    private static async Task Swallow(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            DriftLog.NotRun(what, ex.Message);
        }
    }

    // ── Helpers used by the tests ───────────────────────────────────────────────

    /// <summary>
    /// Trailing-slash-proof base URI. HttpClient resolves the client's relative
    /// "api/..." paths against this, so it must end in exactly one slash.
    /// </summary>
    public static Uri Normalize(string baseUrl) => new(baseUrl.TrimEnd('/') + "/");

    /// <summary>A raw request with explicit control over the Authorization header, for the auth-model assertions.</summary>
    public async Task<HttpStatusCode> RawStatusAsync(string path, string? bearer)
    {
        using var http = new HttpClient { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var resp = await http.SendAsync(request);
        return resp.StatusCode;
    }

    /// <summary>
    /// Status plus raw body. Needed where the client is *tolerant* of a missing
    /// property: a client that silently returns an empty list cannot tell us the
    /// payload changed, so those endpoints are asserted against the raw JSON.
    /// </summary>
    public async Task<(HttpStatusCode Status, string Body)> RawGetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var resp = await _http!.SendAsync(request);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    /// <summary>A unique, obviously-disposable name for an object this suite creates.</summary>
    public static string Throwaway(string what) => $"{ThrowawayPrefix}{what}-{Guid.NewGuid():N}"[..40];
}

/// <summary>
/// Credential resolution: real environment variables win; a .env file found by
/// walking up from the test binary is the local-developer fallback, so
/// `dotnet test` works without remembering `set -a; source .env; set +a`.
/// </summary>
internal static class EnvFile
{
    internal sealed record Settings(string? Email, string? Password, string? BaseUrl);

    /// <summary>
    /// Explicit kill switch. Two uses: turning the live suite off in CI without
    /// deleting the repository secrets, and proving the env-gated path locally —
    /// where the .env walk-up would otherwise always find credentials, because
    /// the developer's .env is right there in the repo root.
    /// </summary>
    internal static bool Disabled =>
        string.Equals(Environment.GetEnvironmentVariable("INTERLINEDLIST_CONTRACT_TESTS"), "off", StringComparison.OrdinalIgnoreCase);

    internal static Settings Resolve()
    {
        if (Disabled)
            return new Settings(null, null, null);

        var email = Environment.GetEnvironmentVariable("INTERLINEDLIST_EMAIL");
        var password = Environment.GetEnvironmentVariable("INTERLINEDLIST_PASSWORD");
        var baseUrl = Environment.GetEnvironmentVariable("INTERLINEDLIST_API_BASE_URL");

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            return new Settings(email, password, baseUrl);

        var fromFile = ReadDotEnv();
        return new Settings(
            string.IsNullOrWhiteSpace(email) ? Get("INTERLINEDLIST_EMAIL") : email,
            string.IsNullOrWhiteSpace(password) ? Get("INTERLINEDLIST_PASSWORD") : password,
            string.IsNullOrWhiteSpace(baseUrl) ? Get("INTERLINEDLIST_API_BASE_URL") : baseUrl);

        string? Get(string key) => fromFile.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    private static Dictionary<string, string> ReadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                foreach (var line in File.ReadAllLines(candidate))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#')
                        continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    var key = trimmed[..eq].Trim();
                    var value = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
                    result[key] = value;
                }

                return result;
            }

            dir = dir.Parent;
        }

        return result;
    }
}

[CollectionDefinition(Name)]
public sealed class ContractCollection : ICollectionFixture<ContractEnvironment>
{
    public const string Name = "live-api-contract";
}
