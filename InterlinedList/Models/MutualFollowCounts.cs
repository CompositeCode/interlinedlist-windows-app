namespace InterlinedList.Models;

/// <summary>
/// How many follows two accounts have in common, from
/// <c>GET /api/follow/{userId}/mutual</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Counts only — there is no endpoint that lists the mutual users.</b>
/// Verified live 2026-09-16 across both documented parameter forms:
/// </para>
/// <code>
/// GET /api/follow/{userId}/mutual                      -> {"mutualFollowers":1,"mutualFollowing":1}
/// GET /api/follow/{userId}/mutual?otherUserId={other}  -> {"mutualFollowers":1,"mutualFollowing":1}
/// </code>
/// <para>
/// So clickable "mutual connections" chips cannot be built — don't reinstate
/// them against this endpoint. The previous <c>GetMutualAsync</c> asked for an
/// array under a <c>mutual</c> key, which is absent, and the tolerant
/// array reader turned that into an empty list: the feature rendered nothing
/// and reported no error. See #160.
/// </para>
/// </remarks>
public sealed class MutualFollowCounts
{
    /// <summary>Accounts that follow both the viewer and the profile.</summary>
    public int MutualFollowers { get; init; }

    /// <summary>Accounts both the viewer and the profile follow.</summary>
    public int MutualFollowing { get; init; }

    public bool HasAny => MutualFollowers > 0 || MutualFollowing > 0;

    /// <summary>A single human line, or null when there is nothing to say.</summary>
    public string? Summary => HasAny
        ? $"{MutualFollowing} mutual following · {MutualFollowers} mutual followers"
        : null;

    public static MutualFollowCounts None => new();
}
