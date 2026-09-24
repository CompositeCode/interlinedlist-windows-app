using System.Net.Http;

namespace InterlinedList.Services;

/// <summary>
/// Blog newsletter subscription.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no public blog-read endpoint.</b> Verified live 2026-09-16:
/// <c>/api/blog</c>, <c>/api/blog/posts</c> and <c>/api/blog/list</c> all
/// return <c>404</c>, and the only blog routes in the spec besides these
/// subscription ones are <c>/api/admin/blog*</c> — admin-only and
/// cookie-authed. So reading the blog is a browser handoff (#121); there is
/// nothing to fetch, and scraping the website's HTML is not a substitute for
/// an API.
/// </para>
/// <para>
/// <b>Unsubscribe is token-based, not email-based.</b> Both
/// <c>GET</c> and <c>POST /api/blog/unsubscribe</c> take a <c>?token=</c>
/// query parameter and nothing else — the token comes from the email footer
/// (the <c>POST</c> form is the RFC-8058 one-click target). A client that only
/// knows the user's address therefore <b>cannot</b> unsubscribe them, so that
/// is a handoff too rather than a button that can't work.
/// </para>
/// </remarks>
public sealed partial class InterlinedApiClient
{
    /// <summary>
    /// Subscribe an address to the blog newsletter. Double opt-in — a
    /// confirmation email follows, and the subscription isn't live until its
    /// link is clicked.
    /// </summary>
    /// <remarks>
    /// Request shape is <c>{email}</c>. The server validates it:
    /// an invalid address returns
    /// <c>400 {"error":"A valid email is required","code":"bad_request"}</c>
    /// (verified live with <c>not-an-email</c> — no message was sent to anyone).
    /// The success envelope is <b>not</b> verified: confirming it would mean
    /// mailing a real address, so nothing is parsed from it.
    /// </remarks>
    public Task SubscribeToBlogAsync(string email, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, "api/blog/subscribe", new { email }, ct);

    /// <summary>
    /// Complete a double opt-in confirmation from an emailed token.
    /// </summary>
    /// <remarks>
    /// Included for completeness; in practice the confirmation link is clicked
    /// in the browser from the email, which is where it lands anyway.
    /// </remarks>
    public Task ConfirmBlogSubscriptionAsync(string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Get, $"api/blog/subscribe/confirm?token={Uri.EscapeDataString(token)}", null, ct);

    /// <summary>
    /// Unsubscribe using a token from an email footer.
    /// </summary>
    /// <remarks>
    /// Only usable if the caller actually has the token — see the type-level
    /// remarks. Do not surface this as "unsubscribe me"; the app has no way to
    /// obtain the token.
    /// </remarks>
    public Task UnsubscribeFromBlogAsync(string token, CancellationToken ct = default)
        => SendVoidAsync(HttpMethod.Post, $"api/blog/unsubscribe?token={Uri.EscapeDataString(token)}", new { }, ct);

    /// <summary>The blog's web address, for the browser handoff.</summary>
    public static string BlogUrl => $"{ApiConfig.BaseUrl}blog";
}
