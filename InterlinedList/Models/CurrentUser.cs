using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// The signed-in user, from <c>GET /api/user</c>.
/// </summary>
/// <remarks>
/// Reconciled against the live payload 2026-09-16. Most of the fields below are
/// <b>preferences the user sets on the web</b>; modelling them is only half the
/// job — each one has to be honored at its consumption point, or the app
/// silently ignores a setting the user changed. See #46.
/// </remarks>
public sealed class CurrentUser
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public string? Bio { get; init; }
    public bool EmailVerified { get; init; }
    public bool DefaultPubliclyVisible { get; init; }
    public bool IsPrivateAccount { get; init; }
    public string? CustomerStatus { get; init; }
    public bool IsAdministrator { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    // ── Account standing ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>new</c> (on probation), <c>active</c>, <c>restricted</c>,
    /// <c>suspended</c> or <c>banned</c>. Drives what the account may do, on a
    /// separate axis from the subscription tier.
    /// </summary>
    public string? AccountStatus { get; init; }

    /// <summary>Set once the account has been reviewed/cleared.</summary>
    public bool Cleared { get; init; }

    /// <summary>An email change awaiting confirmation, else null.</summary>
    public string? PendingEmail { get; init; }

    // ── Preferences ─────────────────────────────────────────────────────────────

    /// <summary><c>light</c>, <c>dark</c> or <c>system</c>. Wins over the OS setting unless <c>system</c>.</summary>
    public string? Theme { get; init; }

    /// <summary>Per-message character limit. Defaults to 666 server-side, and may be below the server cap.</summary>
    public int MaxMessageLength { get; init; }

    /// <summary>Feed page size, 10–30.</summary>
    public int MessagesPerPage { get; init; }

    /// <summary>
    /// Which messages the home feed shows. Live value is snake_case, e.g.
    /// <c>all_messages</c>; the documented options are My Messages, All
    /// Messages, Followers Only and Following Only.
    /// </summary>
    public string? ViewingPreference { get; init; }

    /// <summary>Whether to render link preview cards.</summary>
    public bool ShowPreviews { get; init; }

    /// <summary>How many notifications the bell tray holds, 10–40 (default 20).</summary>
    public int NotificationTrayLimit { get; init; }

    /// <summary>Whether the composer shows the gear for media and cross-posting.</summary>
    public bool ShowAdvancedPostSettings { get; init; }

    /// <summary>Optional profile latitude, used by the location-aware widgets.</summary>
    public double? Latitude { get; init; }

    /// <summary>Optional profile longitude.</summary>
    public double? Longitude { get; init; }

    /// <summary>Default repository for GitHub-backed list creation, when set.</summary>
    public string? GithubDefaultRepo { get; init; }

    // ── Opaque / pass-through ───────────────────────────────────────────────────

    /// <summary>
    /// Saved dashboard layout. Web-oriented structure kept as raw JSON so it
    /// round-trips untouched — the app must not invent its own schema for it.
    /// </summary>
    public JsonElement? DashboardLayout { get; init; }

    /// <summary>Saved front-wall layout. Raw JSON, same reasoning as <see cref="DashboardLayout"/>.</summary>
    public JsonElement? FrontWallLayout { get; init; }

    /// <summary>Notification preferences, when the user payload inlines them. Raw JSON;
    /// the typed surface is <c>GET /api/user/notification-preferences</c>.</summary>
    public JsonElement? NotificationPreferences { get; init; }

    /// <summary>Stripe customer id, when the account has one. Read-only here — billing is a browser handoff.</summary>
    public string? StripeCustomerId { get; init; }

    // ── Derived ─────────────────────────────────────────────────────────────────

    public string DisplayNameOrUsername => DisplayName ?? Username;

    /// <summary>An active subscription — the gate on media, cross-posting, scheduling, AI and creates.</summary>
    public bool IsSubscriber =>
        string.Equals(CustomerStatus, "subscriber", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A brand-new account on probation. Per the product docs this locks direct
    /// messages, image and video upload, cross-posting, scheduled posts, and
    /// creating lists/documents/organizations, and rate-limits plain posting.
    /// </summary>
    public bool IsProbationary =>
        string.Equals(AccountStatus, "new", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Restricted or suspended: sign-in, reading and browsing work, but posting,
    /// replying, reacting, following, messaging and creating do not.
    /// </summary>
    public bool IsReadOnly =>
        string.Equals(AccountStatus, "restricted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(AccountStatus, "suspended", StringComparison.OrdinalIgnoreCase);

    /// <summary>The account is closed.</summary>
    public bool IsBanned =>
        string.Equals(AccountStatus, "banned", StringComparison.OrdinalIgnoreCase);

    /// <summary>Anything other than a normal, active account — worth telling the user about.</summary>
    public bool NeedsStatusBanner =>
        AccountStatus is { Length: > 0 } && !string.Equals(AccountStatus, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>A profile location is set, so the location-aware widgets can work.</summary>
    public bool HasLocation => Latitude is not null && Longitude is not null;

    /// <summary>Feed page size to actually use, clamped to the documented 10–30 and defaulted when unset.</summary>
    public int EffectiveMessagesPerPage => MessagesPerPage is >= 10 and <= 30 ? MessagesPerPage : 20;

    /// <summary>Tray limit to actually use, clamped to the documented 10–40.</summary>
    public int EffectiveNotificationTrayLimit => NotificationTrayLimit is >= 10 and <= 40 ? NotificationTrayLimit : 20;
}
