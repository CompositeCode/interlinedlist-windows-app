namespace InterlinedList.Models;

/// <summary>
/// The per-user AI quota: 50 generations per rolling 24 hours (documented).
/// Both phases count — a /suggest and its follow-up /generate are two
/// generations — and failed attempts (invalid_ai_output, refused,
/// provider_error) count too, so a UI should not auto-retry.
///
/// Shape note (verified live 2026-09-16): GET /api/ai/status returns
/// { usedToday, dailyLimit, remaining }, but the quota echoed by
/// POST /api/ai/suggest returns only { usedToday, dailyLimit } — no
/// "remaining". Hence <see cref="Remaining"/> is nullable and
/// <see cref="RemainingOrComputed"/> is what UI should bind to.
/// </summary>
public sealed class AiQuota
{
    public int UsedToday { get; init; }
    public int DailyLimit { get; init; }

    /// <summary>Only GET /api/ai/status sends this; /suggest and /generate omit it.</summary>
    public int? Remaining { get; init; }

    public int RemainingOrComputed => Remaining ?? Math.Max(0, DailyLimit - UsedToday);

    public bool IsExhausted => DailyLimit > 0 && RemainingOrComputed <= 0;

    public string Display => $"{UsedToday} of {DailyLimit} used today";
}
