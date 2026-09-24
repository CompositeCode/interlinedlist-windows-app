using System.Net;
using Xunit;

namespace InterlinedList.Contract.Tests;

/// <summary>
/// The auth model itself, asserted rather than remembered.
///
/// This app is a native client: it has a bearer sync-token and structurally
/// cannot obtain a cookie session. So "does this endpoint accept the bearer
/// token?" decides whether a feature is buildable at all — and getting that
/// answer wrong in a doc is expensive in both directions. CLAUDE.md's
/// constraint #1 listed endpoints as 401-walled that now answer 200, which
/// discouraged work that was perfectly possible.
///
/// Hence: assert the auth model per endpoint, on a schedule, and let the drift
/// report say when it changes.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class AuthModelTests
{
    private readonly ContractEnvironment _env;

    public AuthModelTests(ContractEnvironment env) => _env = env;

    /// <summary>Endpoints the app treats as bearer-authenticated: bearer => 200, nothing/garbage => 401.</summary>
    public static IEnumerable<object[]> BearerAuthenticated() => new[]
    {
        new object[] { "api/user" },
        new object[] { "api/user/sessions" },
        new object[] { "api/user/identities" },
        new object[] { "api/lists" },
        new object[] { "api/documents" },
        new object[] { "api/notifications?scope=tray&limit=1" },
        new object[] { "api/dm/recipients" },
    };

    [SkippableTheory]
    [MemberData(nameof(BearerAuthenticated))]
    public async Task Bearer_token_is_accepted_and_required(string path)
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);
        var endpoint = $"AUTH GET {path}";

        var withToken = await _env.RawStatusAsync(path, _env.Token);
        var withNothing = await _env.RawStatusAsync(path, bearer: null);
        var withGarbage = await _env.RawStatusAsync(path, "not-a-real-token");

        var problems = new List<string>();

        if (withToken != HttpStatusCode.OK)
            problems.Add($"a valid bearer token got {(int)withToken} instead of 200 — the app cannot use this endpoint any more");
        if (withNothing == HttpStatusCode.OK)
            problems.Add("an unauthenticated request got 200 — this endpoint is no longer protected");
        if (withGarbage == HttpStatusCode.OK)
            problems.Add("an invalid bearer token got 200 — tokens are not being validated");

        if (problems.Count > 0)
        {
            DriftLog.Drift(endpoint, string.Join("; ", problems));
            Assert.Fail($"{path}: {string.Join("; ", problems)}");
        }

        DriftLog.Ok(endpoint, $"bearer=200, anonymous={(int)withNothing}, invalid={(int)withGarbage}");
    }

    /// <summary>
    /// Two of the app's reads are genuinely anonymous: the public feed and the
    /// organization directory both answer 200 with no Authorization header at
    /// all. Pinned as facts, for two reasons. Someone who assumes every endpoint
    /// needs the token will read an unauthenticated 200 as a security hole; and
    /// someone who assumes these are protected will put them in the list above
    /// and get a confusing failure. Both are the documented behaviour.
    /// </summary>
    public static IEnumerable<object[]> AnonymouslyReadable() => new[]
    {
        new object[] { "api/messages?limit=1" },
        new object[] { "api/organizations?limit=1" },
    };

    [SkippableTheory]
    [MemberData(nameof(AnonymouslyReadable))]
    public async Task Public_reads_need_no_token(string path)
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);
        var endpoint = $"AUTH GET {path} (anonymous)";

        var anonymous = await _env.RawStatusAsync(path, bearer: null);
        var withToken = await _env.RawStatusAsync(path, _env.Token);

        if (anonymous != HttpStatusCode.OK)
        {
            DriftLog.Drift(endpoint, $"this public read now requires auth (got {(int)anonymous}) — it used to answer 200 anonymously");
            Assert.Fail($"GET {path} returned {(int)anonymous} without a token; it was an anonymous read.");
        }

        // It must still work *with* a token too, since that is how the app calls it.
        Assert.Equal(HttpStatusCode.OK, withToken);
        DriftLog.Ok(endpoint, "200 with no Authorization header, and 200 with one — a public read");
    }

    /// <summary>
    /// CLAUDE.md's constraint #1 records these two as returning 401 to a bearer
    /// token, "re-probed live 2026-07-31", and concludes a native client
    /// structurally cannot reach them. As of this suite's first run that is
    /// FALSE: both answer 200 to the bearer token and 401 without it — they are
    /// ordinary bearer-authenticated endpoints, and the engagement/dashboard
    /// features they back are buildable.
    ///
    /// Asserted here rather than left in prose, so the day it changes back a
    /// test says so.
    /// </summary>
    public static IEnumerable<object[]> PreviouslyDocumentedAsCookieOnly() => new[]
    {
        new object[] { "api/user/engagement" },
        new object[] { "api/user/dashboard-layout" },
    };

    [SkippableTheory]
    [MemberData(nameof(PreviouslyDocumentedAsCookieOnly))]
    public async Task Endpoints_documented_as_cookie_only_do_accept_the_bearer_token(string path)
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);
        var endpoint = $"AUTH GET {path} (documented as cookie-only)";

        var withToken = await _env.RawStatusAsync(path, _env.Token);
        var withNothing = await _env.RawStatusAsync(path, bearer: null);

        if (withToken != HttpStatusCode.OK)
        {
            DriftLog.Drift(endpoint, $"bearer token got {(int)withToken}; CLAUDE.md's 401 claim may have become true again");
            Assert.Fail($"{path} returned {(int)withToken} to a valid bearer token.");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, withNothing);
        DriftLog.Ok(
            endpoint,
            "bearer=200, anonymous=401 — a normal bearer-authenticated endpoint. CLAUDE.md's \"401 with a bearer token\" note is stale.");
    }

    /// <summary>
    /// The trailing slash in INTERLINEDLIST_API_BASE_URL is a real trap: naive
    /// string concatenation produces a double slash and the server answers with
    /// a 308 redirect instead of the payload, which reads as a dead endpoint.
    /// This pins both the trap and the fix.
    /// </summary>
    [SkippableFact]
    public async Task Trailing_slash_in_the_base_url_does_not_become_a_308()
    {
        Skip.IfNot(_env.IsConfigured, _env.SkipReason);

        // Normalisation collapses any number of trailing slashes to exactly one.
        Assert.Equal(
            ContractEnvironment.Normalize("https://interlinedlist.com/"),
            ContractEnvironment.Normalize("https://interlinedlist.com"));

        var ok = await _env.RawStatusAsync("api/user", _env.Token);
        Assert.Equal(HttpStatusCode.OK, ok);

        // The double slash the naive form produces really does redirect.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var resp = await http.GetAsync($"{_env.BaseUri.ToString().TrimEnd('/')}//api/user");
        Assert.Equal(HttpStatusCode.PermanentRedirect, resp.StatusCode);

        DriftLog.Ok(
            "AUTH base-url normalisation",
            "single-slash base resolves to 200; the double slash a naive concat produces still yields 308");
    }
}
