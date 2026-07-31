using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// Read-only wrapper around a single <see cref="DirectMessage"/> for the DM
/// thread list. Plain properties are enough — a message never mutates in place;
/// the whole collection is rebuilt on each thread (re-)load.
/// </summary>
public sealed class DmMessageViewModel
{
    private readonly DirectMessage _message;

    public DmMessageViewModel(DirectMessage message, string? currentUserId)
    {
        _message = message;
        IsMine = message.SenderId == currentUserId;
    }

    public string Body => _message.Body;
    public string TimeFormatted => _message.TimeFormatted;
    public bool IsMine { get; }
}
