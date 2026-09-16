using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// One shared, lazily-loaded map of list id → <see cref="GitHubListBacking"/>,
/// so that marking GitHub-backed lists in a lists browser costs <b>one</b>
/// request rather than one per row.
///
/// <para>
/// A per-row <c>GET /api/lists/{id}</c> would be the obvious way to give each
/// badge its own data, and it is exactly the wrong one: a user with fifty lists
/// would fire fifty requests to draw fifty tiny labels. <c>GET /api/lists?all=1</c>
/// already returns <c>source</c>/<c>githubRepo</c>/<c>githubRepoPrivate</c> for
/// every list (verified live 2026-09-16), so one call answers every badge.
/// </para>
///
/// <para>
/// Deliberately <b>not</b> registered in <see cref="AppServices"/>: it is a cache
/// in front of an existing singleton, has no other collaborators, and adding it
/// there would touch a file three other open branches also touch. It resolves
/// <see cref="AppServices.Session"/> itself at call time.
/// </para>
///
/// <para>
/// UI-thread affine by design. Every caller is a WPF control, loads are awaited
/// on the dispatcher, and <see cref="Changed"/> is raised on whichever thread
/// completed the load — which, with the default synchronization context, is the
/// UI thread. Concurrent loads are collapsed onto one in-flight task rather than
/// locked, so two badges appearing at once make one request.
/// </para>
/// </summary>
public static class GitHubListIndex
{
    private static readonly Dictionary<string, GitHubListBacking> Cache = new(StringComparer.Ordinal);
    private static Task? _inFlight;
    private static bool _loaded;

    /// <summary>Raised whenever the cache changes, so live badges can re-read it.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// The backing for one list, or null if the index hasn't been loaded or
    /// doesn't know that list. Null means "don't know yet" — never "local" — so
    /// callers must not draw a conclusion from it.
    /// </summary>
    public static GitHubListBacking? Get(string? listId) =>
        listId is { Length: > 0 } id && Cache.TryGetValue(id, out var backing) ? backing : null;

    /// <summary>
    /// Loads the index once. Repeat calls are free; concurrent calls share the
    /// single in-flight request. Failures are swallowed — a missing badge is not
    /// worth an error banner — but leave <see cref="_loaded"/> false so a later
    /// call retries.
    /// </summary>
    public static Task EnsureLoadedAsync()
    {
        if (_loaded) return Task.CompletedTask;
        return _inFlight ??= LoadAsync();
    }

    /// <summary>Forces a reload, e.g. after creating a list or refreshing one from GitHub.</summary>
    public static Task RefreshAsync()
    {
        _loaded = false;
        _inFlight = null;
        return EnsureLoadedAsync();
    }

    /// <summary>
    /// Writes one freshly-read list straight into the cache — used after a
    /// "Refresh from GitHub", where the list's <c>githubRepoPrivate</c> may have
    /// just changed and re-reading every list would be wasteful.
    /// </summary>
    public static void Put(GitHubListBacking backing)
    {
        if (backing.ListId is not { Length: > 0 }) return;
        Cache[backing.ListId] = backing;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static async Task LoadAsync()
    {
        try
        {
            var backings = await AppServices.Session.Api.GetListGitHubBackingsAsync();

            Cache.Clear();
            foreach (var backing in backings)
            {
                if (backing.ListId is { Length: > 0 })
                    Cache[backing.ListId] = backing;
            }

            _loaded = true;
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InterlinedApiException or System.Net.Http.HttpRequestException
                                       or System.Text.Json.JsonException)
        {
            // A badge that can't be drawn is not an error the user needs; the next
            // EnsureLoadedAsync will try again. Logged so it isn't invisible.
            AppLog.Warn($"GitHubListIndex load failed: {ex.Message}");
        }
        finally
        {
            _inFlight = null;
        }
    }
}
