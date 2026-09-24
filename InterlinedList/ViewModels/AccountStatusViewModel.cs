using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The account-status banner's content, computed from <c>accountStatus</c>. One
/// instance per <see cref="CurrentUser"/> snapshot — it's immutable, so a
/// session refresh replaces it rather than mutating it.
/// </summary>
/// <remarks>
/// <para>
/// The web shows this at the top of the home page. The shell
/// (<c>MainWindow</c>) is the right home for it here too, but that file is in
/// flight (#149), so #50 lands the banner at the top of Settings — a real,
/// visible banner somewhere beats a correct one nowhere. Moving it is a matter
/// of dropping the same block into the shell and binding to this type.
/// </para>
/// <para>
/// <b>Not live-verified:</b> the shared test account is <c>active</c>, so every
/// branch below except the hidden one was checked by constructing the status
/// locally, not by observing a real restricted account.
/// </para>
/// </remarks>
public sealed class AccountStatusViewModel
{
    /// <summary>Amber — a temporary state the user can work their way out of.</summary>
    public const string SeverityWarning = "warning";

    /// <summary>Red — posting and creating are gone until someone intervenes.</summary>
    public const string SeverityDanger = "danger";

    public AccountStatusViewModel(CurrentUser? user)
    {
        // Capability gates are populated for every account, including `active`,
        // so a consumer can bind to them unconditionally and get "allowed".
        CanPost = AccountCapabilities.IsAllowed(user, AccountCapability.Post);
        CanReply = AccountCapabilities.IsAllowed(user, AccountCapability.Reply);
        CanReact = AccountCapabilities.IsAllowed(user, AccountCapability.React);
        CanFollow = AccountCapabilities.IsAllowed(user, AccountCapability.Follow);
        CanDirectMessage = AccountCapabilities.IsAllowed(user, AccountCapability.DirectMessage);
        CanUploadMedia = AccountCapabilities.IsAllowed(user, AccountCapability.MediaUpload);
        CanCrossPost = AccountCapabilities.IsAllowed(user, AccountCapability.CrossPost);
        CanSchedulePosts = AccountCapabilities.IsAllowed(user, AccountCapability.ScheduledPost);
        CanCreateContent = AccountCapabilities.IsAllowed(user, AccountCapability.CreateContent);

        PostBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.Post);
        ReplyBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.Reply);
        ReactBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.React);
        FollowBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.Follow);
        DirectMessageBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.DirectMessage);
        MediaUploadBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.MediaUpload);
        CrossPostBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.CrossPost);
        ScheduledPostBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.ScheduledPost);
        CreateContentBlockedReason = AccountCapabilities.BlockedReason(user, AccountCapability.CreateContent);

        IsVisible = user?.NeedsStatusBanner == true;
        if (user is null || !IsVisible) return;

        LockedFeatures = string.Join(" · ", AccountCapabilities.LockedFeatures(user));

        if (user.IsBanned)
        {
            Severity = SeverityDanger;
            Headline = "This account is closed";
            Detail = "Sign-in is disabled and nothing can be posted or changed. If you think this is a mistake, you can appeal.";
            ActionLabel = "Appeal on the web";
            ActionUrl = HelpUrl;
        }
        else if (user.IsReadOnly)
        {
            var suspended = string.Equals(user.AccountStatus, "suspended", StringComparison.OrdinalIgnoreCase);
            Severity = SeverityDanger;
            Headline = suspended ? "This account is suspended" : "This account is restricted";
            Detail = suspended
                ? "The team has put the account in read-only mode. You can sign in, read and browse, but posting, replying, digging, following, messaging and creating are turned off. This is appealable."
                : "The account is temporarily read-only while it's reviewed. You can sign in, read and browse, but posting, replying, digging, following, messaging and creating are turned off. This is appealable.";
            ActionLabel = "Appeal on the web";
            ActionUrl = HelpUrl;
        }
        else if (user.IsProbationary)
        {
            Severity = SeverityWarning;
            Headline = "Your account is on probation";
            Detail = user.EmailVerified
                ? "Reading, browsing, following, blocking, muting and reporting all work, and you can post a few times an hour. A handful of features stay locked until the account clears."
                : "Reading, browsing, following, blocking, muting and reporting all work, and you can post a few times an hour. Verifying your email address is the fastest way off probation.";
            // POST /api/auth/send-verification-email is cookie-session only per
            // the OpenAPI spec (`x-auth-type: session`), so a bearer-token
            // client structurally can't trigger it — same browser handoff the
            // app already uses for billing and OAuth linking.
            ActionLabel = user.EmailVerified ? "Read about account status" : "Verify your email on the web";
            ActionUrl = user.EmailVerified ? HelpUrl : ApiConfig.BaseUrl;
        }
        else
        {
            // A status the server added since this was written. Say so plainly
            // rather than guessing what it restricts.
            Severity = SeverityWarning;
            Headline = $"Your account status is \"{user.AccountStatus}\"";
            Detail = "Some features may be limited. Check your account on the web for the details.";
            ActionLabel = "Read about account status";
            ActionUrl = HelpUrl;
        }
    }

    private const string HelpUrl = ApiConfig.BaseUrl + "help/account";

    /// <summary>False for a normal <c>active</c> account — the banner collapses entirely.</summary>
    public bool IsVisible { get; }

    public string Severity { get; } = SeverityWarning;
    public string Headline { get; } = "";
    public string Detail { get; } = "";

    /// <summary>Everything the status takes away, pre-joined for display. Empty when nothing is.</summary>
    public string LockedFeatures { get; } = "";

    public bool HasLockedFeatures => LockedFeatures.Length > 0;

    public string? ActionLabel { get; }
    public string? ActionUrl { get; }

    // ── Capability gates ────────────────────────────────────────────────────────
    // Bind a locked action's IsEnabled to Can*, and its ToolTip to the matching
    // *BlockedReason (null when allowed, so the tooltip simply doesn't show).
    // This is the "disable up front with the reason" half of #50; the actions
    // themselves live in the feed/shell/compose files that #149 owns.

    public bool CanPost { get; }
    public bool CanReply { get; }
    public bool CanReact { get; }
    public bool CanFollow { get; }
    public bool CanDirectMessage { get; }
    public bool CanUploadMedia { get; }
    public bool CanCrossPost { get; }
    public bool CanSchedulePosts { get; }
    public bool CanCreateContent { get; }

    public string? PostBlockedReason { get; }
    public string? ReplyBlockedReason { get; }
    public string? ReactBlockedReason { get; }
    public string? FollowBlockedReason { get; }
    public string? DirectMessageBlockedReason { get; }
    public string? MediaUploadBlockedReason { get; }
    public string? CrossPostBlockedReason { get; }
    public string? ScheduledPostBlockedReason { get; }
    public string? CreateContentBlockedReason { get; }
}
