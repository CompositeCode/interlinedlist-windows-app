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

    public string Id => _message.Id;
    public string Body => _message.Body;
    public string TimeFormatted => _message.TimeFormatted;
    public bool IsMine { get; }

    public IReadOnlyList<string> ImageUrls => _message.ImageUrls ?? (IReadOnlyList<string>)Array.Empty<string>();
    public bool HasImages => _message.ImageUrls is { Count: > 0 };

    // A message is "trashed" from this user's perspective when it's their own
    // and they've soft-deleted it (SenderDeletedAt). The bubble dims/relabels.
    //
    // CAVEAT (live-verified 2026-09-15): in practice this is ALWAYS false. The
    // API never serializes the per-side delete timestamps — see the remarks on
    // DirectMessage — so trash state is knowable only from WHICH folder returned
    // a message. The Deleted tab is therefore the real restore surface; this
    // property survives only as the in-thread dim/relabel hook for if the server
    // ever starts exposing the field.
    public bool IsTrashed => IsMine && _message.SenderDeletedAt is not null;
}
