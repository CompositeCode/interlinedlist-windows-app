namespace InterlinedList.Models;

/// <summary>
/// The three personal DM folders the product exposes as tabs.
///
/// Selected by the <c>folder</c> query parameter on <c>GET /api/dm</c> —
/// documented on /help/api/direct-messages ("Query: folder (inbox | sent |
/// deleted, default inbox), cursor, take") and in the OpenAPI spec, and
/// live-verified 2026-09-15: the three values below return 200, while any other
/// value returns 400 {"error":"folder must be one of: inbox, sent, deleted.",
/// "code":"bad_request"}.
///
/// Folders are personal because each message carries an independent per-side
/// soft-delete: "inbox" = received and not trashed, "sent" = sent and not
/// trashed, "deleted" = trashed by THIS caller (as either sender or recipient).
/// Trashing never touches the other participant's copy and never changes read
/// state.
/// </summary>
public enum DmFolder
{
    Inbox,
    Sent,
    Deleted
}

public static class DmFolderExtensions
{
    /// <summary>The literal <c>folder=</c> value the API accepts (lowercase).</summary>
    public static string ToWireValue(this DmFolder folder) => folder switch
    {
        DmFolder.Sent => "sent",
        DmFolder.Deleted => "deleted",
        _ => "inbox",
    };

    /// <summary>Tab caption.</summary>
    public static string ToLabel(this DmFolder folder) => folder switch
    {
        DmFolder.Sent => "Sent",
        DmFolder.Deleted => "Deleted",
        _ => "Inbox",
    };

    /// <summary>Empty-state copy, phrased the way /help/direct-messages describes each folder.</summary>
    public static string ToEmptyStateText(this DmFolder folder) => folder switch
    {
        DmFolder.Sent => "Nothing in Sent. Messages you send appear here until you delete your copy.",
        DmFolder.Deleted => "Nothing in Deleted. Messages you remove from Inbox or Sent land here, and you can restore them.",
        _ => "Nothing in your Inbox. Messages other people send you appear here.",
    };
}
