namespace InterlinedList.Models;

/// <summary>
/// Token usage for one AI call, echoed by POST /api/ai/suggest (verified live
/// 2026-09-16: { "inputTokens": 125, "outputTokens": 40, "model":
/// "claude-sonnet-5" }). The server records counts and status only in its audit
/// ledger — never prompt or output text.
/// </summary>
public sealed class AiUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    /// <summary>The model that actually ran (an Anthropic id — AI is Anthropic-only app-wide).</summary>
    public string? Model { get; init; }

    public int TotalTokens => InputTokens + OutputTokens;

    public string Display => Model is { Length: > 0 } m
        ? $"{m} · {InputTokens} in / {OutputTokens} out"
        : $"{InputTokens} in / {OutputTokens} out";
}
