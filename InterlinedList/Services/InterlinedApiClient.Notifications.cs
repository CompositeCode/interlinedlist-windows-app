using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The full notification feed, as distinct from the nav-bell tray.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/notifications</c> has two modes, and the spec spells out the
/// difference: <i>"?scope=tray returns the unread-only nav bell payload; the
/// default is the historical feed of read and unread, newest first."</i>
/// </para>
/// <para>
/// The existing <c>GetNotificationsAsync</c> hard-codes <c>?scope=tray</c>,
/// which is why nothing older than the tray was ever reachable. This partial
/// adds the default scope.
/// </para>
/// <para>
/// <b>The feed does not paginate.</b> Verified live 2026-09-16: <c>offset</c> is
/// silently ignored (<c>?limit=5&amp;offset=5</c> and <c>&amp;offset=35</c> both
/// returned the same first item), and the response carries no <c>pagination</c>
/// block — only <c>{unreadCount, items}</c>. So "load more" means re-fetching
/// with a larger <c>limit</c>, not requesting a next page. An upper cap on
/// <c>limit</c> was not observable: <c>?limit=200</c> returned all 37 items the
/// account has.
/// </para>
/// </remarks>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// The historical notification feed — read and unread, newest first.
    /// </summary>
    /// <param name="limit">
    /// How many to return. Because the endpoint has no offset, this is the only
    /// lever: to show more, ask for more.
    /// </param>
    public Task<NotificationsPage> GetNotificationFeedAsync(int limit = 50, CancellationToken ct = default)
        => GetJsonAsync<NotificationsPage>($"api/notifications?limit={limit}", ct);
}
