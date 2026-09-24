using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// One row in the conversation list (<c>GET /api/dm/conversations</c>) — the
/// default left-rail view, newest activity first. Plain (non-observable)
/// properties are enough: the collection is rebuilt whenever the list reloads.
/// </summary>
public sealed class DmConversationViewModel
{
    private readonly DmConversation _conversation;

    public DmConversationViewModel(DmConversation conversation) => _conversation = conversation;

    /// <summary>Identity of a conversation row — there is no separate id field.</summary>
    public string? PairKey => _conversation.PairKey;

    public DmRecipient? OtherUser => _conversation.OtherUser;
    public string? OtherUsername => _conversation.OtherUser?.Username;
    public string OtherDisplayName => _conversation.OtherUser?.DisplayNameOrUsername ?? "Unknown participant";
    public string OtherHandle => _conversation.OtherUser is { } u ? u.Handle : "";
    public string? OtherAvatar => _conversation.OtherUser?.Avatar;

    /// <summary>
    /// The API's markdown-stripped excerpt of the last message, falling back to
    /// its raw body. Prefixed with "You: " when the caller sent it, which is
    /// what <c>isMine</c> on the row reports.
    /// </summary>
    public string Preview
    {
        get
        {
            var text = _conversation.Preview is { Length: > 0 } p
                ? p
                : _conversation.LastBody ?? "";
            var flattened = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (flattened.Length == 0)
                flattened = _conversation.LastImageUrls is { Count: > 0 } ? "(photo)" : "(no text)";
            return _conversation.IsMine ? $"You: {flattened}" : flattened;
        }
    }

    public string DateTimeFormatted => _conversation.LastCreatedAt is { } at
        ? at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm'Z'")
        : "";

    public int UnreadCount => _conversation.UnreadCount;
    public bool HasUnread => _conversation.UnreadCount > 0;
    public string UnreadLabel => _conversation.UnreadCount > 99 ? "99+" : _conversation.UnreadCount.ToString();

    /// <summary>A conversation can only be opened if the API told us who the other party is.</summary>
    public bool CanOpen => _conversation.OtherUser?.Username is { Length: > 0 };
}
