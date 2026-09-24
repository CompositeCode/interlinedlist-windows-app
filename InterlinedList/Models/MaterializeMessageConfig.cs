using System.Text.Json.Serialization;

namespace InterlinedList.Models;

/// <summary>
/// Channel labels accepted by <see cref="MaterializeMessageConfig.CrossPostTargets"/>.
/// They only <em>size</em> the draft — <see cref="MessageDraft.CharLimit"/> comes back
/// as the smaller of the account's own maxMessageLength and the tightest limit among
/// these. Verified live 2026-09-16 on an account whose maxMessageLength is 666:
/// no targets → 666, ["Bluesky"] → 300, ["Mastodon","X/Twitter"] → 280.
/// </summary>
public static class MaterializeCrossPostTargets
{
    public const string Bluesky = "Bluesky";
    public const string Mastodon = "Mastodon";
    public const string LinkedIn = "LinkedIn";
    public const string Twitter = "X/Twitter";
}

/// <summary>
/// Body/threading configuration for a <see cref="MaterializeTarget.Message"/>
/// materialize. All fields optional.
/// </summary>
public sealed class MaterializeMessageConfig
{
    /// <summary>
    /// An edited body. When omitted the server derives one from the source. Either
    /// way it is re-checked against the limit server-side, so a client cannot hand
    /// back a body longer than the account allows.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    /// <summary>
    /// Channels the draft should be sized for — see <see cref="MaterializeCrossPostTargets"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CrossPostTargets { get; init; }

    /// <summary>
    /// Permission to split an over-limit body into a thread. Defaults to <c>false</c>,
    /// and an over-limit body is <b>rejected</b> with <c>400</c> until it is <c>true</c>
    /// (splitting someone's post without asking isn't a decision the server makes
    /// silently). When true, <see cref="MessageDraft.Thread"/> carries the parts in
    /// reply order. Verified live 2026-09-16.
    /// </summary>
    public bool AllowThread { get; init; }

    /// <summary>
    /// Pass-through to your eventual POST /api/messages call; this endpoint accepts it
    /// but enforces nothing (verified live 2026-09-16 — the draft came back unchanged).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PubliclyVisible { get; init; }

    /// <inheritdoc cref="PubliclyVisible"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <inheritdoc cref="PubliclyVisible"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ScheduledAt { get; init; }
}
