namespace InterlinedList.Services;

/// <summary>
/// CSV data exports. Each endpoint returns a raw text/csv body (verified live
/// 2026-07-31) — callers write it straight to a user-chosen file.
/// </summary>
public sealed partial class InterlinedApiClient
{
    public Task<string> ExportMessagesCsvAsync(CancellationToken ct = default)
        => GetStringAsync("api/exports/messages", ct);

    public Task<string> ExportListsCsvAsync(CancellationToken ct = default)
        => GetStringAsync("api/exports/lists", ct);

    public Task<string> ExportListDataRowsCsvAsync(CancellationToken ct = default)
        => GetStringAsync("api/exports/list-data-rows", ct);

    public Task<string> ExportFollowsCsvAsync(CancellationToken ct = default)
        => GetStringAsync("api/exports/follows", ct);
}
