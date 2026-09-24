using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// One row in the Inbox / Sent / Deleted list (a message from
/// <c>GET /api/dm?folder=…</c>).
///
/// Observable rather than plain because a row mutates in place: opening a
/// conversation marks its messages read server-side, and the row is then
/// re-fetched via <c>GET /api/dm/{id}</c> to clear the unread dot without
/// reloading the whole folder.
/// </summary>
public sealed partial class DmFolderItemViewModel : ObservableObject
{
    private DirectMessage _message;
    private readonly string? _currentUserId;

    public DmFolderItemViewModel(DirectMessage message, string? currentUserId)
    {
        _message = message;
        _currentUserId = currentUserId;
    }

    public string Id => _message.Id;

    /// <summary>True when the caller sent this message (so it is a Sent-side row).</summary>
    public bool IsMine => _message.SenderId == _currentUserId;

    /// <summary>
    /// The other participant: for a message the caller sent that's the
    /// recipient, otherwise the sender. Both arrive nested on folder rows.
    /// </summary>
    public DmRecipient? OtherUser => IsMine ? _message.Recipient : _message.Sender;

    public string OtherDisplayName => OtherUser?.DisplayNameOrUsername ?? "Unknown participant";
    public string OtherHandle => OtherUser is { } u ? u.Handle : "";
    public string? OtherAvatar => OtherUser?.Avatar;

    /// <summary>
    /// The API's markdown-stripped <c>preview</c> when present, else the raw
    /// body. Collapsed to one line so a row never grows to fit a long message.
    /// </summary>
    public string Preview => ToSingleLine(
        _message.Preview is { Length: > 0 } p ? p : _message.Body);

    public string DateTimeFormatted => _message.DateTimeFormatted;

    /// <summary>
    /// A received message the caller has not read yet — the product's blue dot.
    /// A sender can never mark their own message read, so own rows are never unread.
    /// </summary>
    public bool IsUnread => !IsMine && _message.ReadAt is null;

    public bool HasImages => _message.ImageUrls is { Count: > 0 };
    public int ImageCount => _message.ImageUrls?.Count ?? 0;
    public string ImageCountLabel => ImageCount == 1 ? "1 photo" : $"{ImageCount} photos";

    /// <summary>
    /// Replace the backing message after a read-after-write re-fetch
    /// (<c>GET /api/dm/{id}</c>) and re-raise everything derived from it.
    /// </summary>
    public void Update(DirectMessage message)
    {
        _message = message;
        OnPropertyChanged(nameof(IsMine));
        OnPropertyChanged(nameof(OtherUser));
        OnPropertyChanged(nameof(OtherDisplayName));
        OnPropertyChanged(nameof(OtherHandle));
        OnPropertyChanged(nameof(OtherAvatar));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DateTimeFormatted));
        OnPropertyChanged(nameof(IsUnread));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(ImageCountLabel));
    }

    private static string ToSingleLine(string text)
    {
        var flattened = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length == 0 ? "(no text)" : flattened;
    }
}
