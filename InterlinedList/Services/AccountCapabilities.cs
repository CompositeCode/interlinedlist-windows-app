using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>The things an account's <c>accountStatus</c> can take away.</summary>
public enum AccountCapability
{
    Post,
    Reply,
    React,
    Follow,
    DirectMessage,
    MediaUpload,
    CrossPost,
    ScheduledPost,
    CreateContent
}

/// <summary>
/// What the signed-in account is allowed to do, on the <c>accountStatus</c> axis
/// only — subscription tier is a separate gate and is not modelled here.
/// </summary>
/// <remarks>
/// <para>
/// The point of this type is <b>disabling a locked action up front, with the
/// reason</b>, instead of letting the user click and collect a bare 403. Call
/// <see cref="BlockedReason"/> from a <c>CanExecute</c> or a tooltip: it returns
/// null when the action is allowed, and a sentence fit for a user when it isn't.
/// </para>
/// <para>
/// The truth table comes from the product documentation (<c>/help/account</c>),
/// not from probing: the shared test account is <c>accountStatus: "active"</c>,
/// so the non-active branches are <b>not live-verified</b> and were exercised by
/// constructing each status locally. If a real restricted/probationary account
/// ever turns up, re-check the specifics before trusting the copy.
/// </para>
/// </remarks>
public static class AccountCapabilities
{
    /// <summary>
    /// Locked while an account is on probation (<c>new</c>). Plain posting still
    /// works, just rate-limited to a few per hour, which the server enforces.
    /// </summary>
    private static readonly AccountCapability[] ProbationLocked =
    [
        AccountCapability.DirectMessage,
        AccountCapability.MediaUpload,
        AccountCapability.CrossPost,
        AccountCapability.ScheduledPost,
        AccountCapability.CreateContent
    ];

    public static bool IsAllowed(CurrentUser? user, AccountCapability capability)
        => BlockedReason(user, capability) is null;

    /// <summary>
    /// Null when <paramref name="capability"/> is available; otherwise a
    /// user-facing explanation of why it isn't.
    /// </summary>
    public static string? BlockedReason(CurrentUser? user, AccountCapability capability)
    {
        // No session yet: don't claim anything is locked, the caller isn't
        // showing an actionable surface anyway.
        if (user is null) return null;

        if (user.IsBanned)
            return "This account is closed, so nothing can be posted or changed.";

        if (user.IsReadOnly)
            // Case-insensitive to match CurrentUser.IsReadOnly, which is what
            // got us into this branch — otherwise a "Suspended" would be read
            // only *and* described as restricted.
            return string.Equals(user.AccountStatus, "suspended", StringComparison.OrdinalIgnoreCase)
                ? "This account is suspended and read-only while the team reviews it. You can still sign in, read and browse."
                : "This account is restricted and read-only while it's being reviewed. You can still sign in, read and browse.";

        if (user.IsProbationary && Array.IndexOf(ProbationLocked, capability) >= 0)
            return $"{Describe(capability)} unlocks once your account is off probation. Verifying your email address is the fastest way there.";

        return null;
    }

    /// <summary>
    /// Plain-language names of everything the current status takes away, for the
    /// status banner. Empty for a normal account.
    /// </summary>
    public static IReadOnlyList<string> LockedFeatures(CurrentUser? user)
    {
        if (user is null || !user.NeedsStatusBanner) return [];

        if (user.IsBanned || user.IsReadOnly)
            return ["Posting", "Replying", "Digging", "Following", "Direct messages", "Creating lists, documents and organizations"];

        if (user.IsProbationary)
            return ["Direct messages", "Image and video upload", "Cross-posting", "Scheduled posts", "Creating lists, documents and organizations"];

        return [];
    }

    private static string Describe(AccountCapability capability) => capability switch
    {
        AccountCapability.Post => "Posting",
        AccountCapability.Reply => "Replying",
        AccountCapability.React => "Digging",
        AccountCapability.Follow => "Following",
        AccountCapability.DirectMessage => "Direct messages",
        AccountCapability.MediaUpload => "Image and video upload",
        AccountCapability.CrossPost => "Cross-posting",
        AccountCapability.ScheduledPost => "Scheduled posts",
        AccountCapability.CreateContent => "Creating lists, documents and organizations",
        _ => "This feature"
    };
}
