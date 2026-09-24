namespace InterlinedList.Models;

/// <summary>
/// The optional "crossPost" object on POST /api/ai/generate — message_series
/// only. It's the composer's cross-post selection, applied to every scheduled
/// message the series creates. Sanitized server-side against the caller's own
/// connected accounts (so a stale selection can't post anywhere unlinked), and
/// the tightest selected service's character limit also caps generation.
///
/// Ignored for every other feature. Not live-verified — POST /api/ai/generate
/// was deliberately never called against the shared test account (it persists
/// content), so treat the field names as contract-documented rather than proven.
/// </summary>
public sealed class AiCrossPostOptions
{
    public bool CrossPostToBluesky { get; init; }
    public bool CrossPostToTwitter { get; init; }
    public bool CrossPostToLinkedIn { get; init; }

    /// <summary>Mastodon identity ids to post through (the app supports multiple Mastodon accounts).</summary>
    public List<string> SelectedMastodonIds { get; init; } = new();

    /// <summary>LinkedIn posting targets (personal profile vs. an organization page).</summary>
    public List<string> SelectedLinkedInTargets { get; init; } = new();

    /// <summary>LinkedIn convention: put the link in the first comment instead of the post body.</summary>
    public bool LinkedInLinkAsFirstComment { get; init; }

    public bool HasAnySelection =>
        CrossPostToBluesky || CrossPostToTwitter || CrossPostToLinkedIn
        || SelectedMastodonIds.Count > 0 || SelectedLinkedInTargets.Count > 0;

    /// <summary>Only the fields that were actually set — see AiContext.ToWire for why nulls aren't sent.</summary>
    public Dictionary<string, object?> ToWire()
    {
        var wire = new Dictionary<string, object?>();

        if (CrossPostToBluesky) wire["crossPostToBluesky"] = true;
        if (CrossPostToTwitter) wire["crossPostToTwitter"] = true;
        if (CrossPostToLinkedIn) wire["crossPostToLinkedIn"] = true;
        if (SelectedMastodonIds.Count > 0) wire["selectedMastodonIds"] = SelectedMastodonIds;
        if (SelectedLinkedInTargets.Count > 0) wire["selectedLinkedInTargets"] = SelectedLinkedInTargets;
        if (LinkedInLinkAsFirstComment) wire["linkedInLinkAsFirstComment"] = true;

        return wire;
    }

    /// <summary>
    /// The channel labels that correspond to this selection, for
    /// context.channels on the preceding /suggest call (which uses them only to
    /// size the generated messages).
    /// </summary>
    public List<string> ToSeriesChannels()
    {
        var channels = new List<string>();
        if (CrossPostToBluesky) channels.Add(AiSeriesChannels.Bluesky);
        if (SelectedMastodonIds.Count > 0) channels.Add(AiSeriesChannels.Mastodon);
        if (CrossPostToLinkedIn || SelectedLinkedInTargets.Count > 0) channels.Add(AiSeriesChannels.LinkedIn);
        if (CrossPostToTwitter) channels.Add(AiSeriesChannels.Twitter);
        return channels;
    }
}
