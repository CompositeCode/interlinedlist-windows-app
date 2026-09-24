using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// GET /api/limits — the server's published media and content limits.
/// </summary>
/// <remarks>
/// Cached for the process lifetime: these are deployment-wide constants, not
/// per-request data, and every composer keystroke and file picker wants them.
/// Verified live 2026-09-15 (bearer-OK).
/// </remarks>
public sealed partial class InterlinedApiClient
{
    private ApiLimits? _cachedLimits;
    private readonly SemaphoreSlim _limitsGate = new(1, 1);

    /// <summary>
    /// The server's limits, fetched once and cached. Pass
    /// <paramref name="forceRefresh"/> to re-read.
    /// </summary>
    public async Task<ApiLimits> GetLimitsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cachedLimits is { } cached) return cached;

        await _limitsGate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _cachedLimits is { } inner) return inner;
            var limits = await GetJsonAsync<ApiLimits>("api/limits", ct);
            _cachedLimits = limits;
            return limits;
        }
        finally
        {
            _limitsGate.Release();
        }
    }

    /// <summary>
    /// The limits if they've already been fetched, otherwise null. For UI that
    /// wants to render without awaiting — call <see cref="GetLimitsAsync"/> once
    /// at startup and this stays warm afterward.
    /// </summary>
    public ApiLimits? CachedLimits => _cachedLimits;
}
