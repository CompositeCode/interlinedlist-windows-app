namespace InterlinedList.Models;

/// <summary>
/// The result of a <see cref="MaterializeTarget.Message"/> materialize: a post
/// draft, and nothing written. Shape verified live 2026-09-16 — the response is
/// <c>201</c> with <c>{ "message": { content, thread, isThread, charLimit } }</c>
/// and exactly those four keys.
/// <para>
/// Publish it with POST /api/messages (see
/// <c>InterlinedApiClient.PostMessageAsync</c>) — that endpoint is where every
/// posting gate lives (account status, verified email, subscriber checks for
/// media/cross-posting/scheduling, probation rate limits, maxMessageLength), so
/// a prefill deliberately stops at the draft rather than bypassing them.
/// </para>
/// </summary>
public sealed class MessageDraft
{
    /// <summary>The whole draft body, over-limit included when <see cref="IsThread"/>.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The parts to post, in reply order. A single-element list holding
    /// <see cref="Content"/> when the draft fits in one post.
    /// </summary>
    public List<string> Thread { get; init; } = [];

    /// <summary>True when <see cref="Content"/> was split across <see cref="Thread"/>.</summary>
    public bool IsThread { get; init; }

    /// <summary>
    /// The smaller of the account's maxMessageLength (GET /api/user) and the tightest
    /// limit among the requested cross-post targets. Sizing to the account limit alone
    /// would show one post and then silently send a multi-part thread to Bluesky.
    /// </summary>
    public int CharLimit { get; init; }

    /// <summary>How many posts publishing this draft takes.</summary>
    public int PartCount => Thread.Count > 0 ? Thread.Count : 1;

    /// <summary>Convenience for a "this will post as N parts" hint.</summary>
    public bool FitsInOnePost => !IsThread;
}
