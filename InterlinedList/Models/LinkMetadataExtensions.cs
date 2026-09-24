namespace InterlinedList.Models;

/// <summary>
/// Helpers over the <c>linkMetadata</c> envelope that live outside
/// <see cref="Message"/> itself.
/// </summary>
public static class LinkMetadataExtensions
{
    /// <summary>
    /// Every link worth drawing a card for: fetched successfully <b>and</b>
    /// carrying metadata. A failed unfurl arrives as
    /// <c>{ url, platform, fetchStatus: "failed" }</c> with no <c>metadata</c> at
    /// all, so rendering it would produce an empty card.
    /// </summary>
    /// <remarks>
    /// Same predicate as <see cref="LinkMetadataEnvelope.Primary"/>, which returns
    /// the first match — used for the ad-hoc single-URL compose preview. This
    /// returns all of them, because a message can carry more than one unfurled
    /// link (1 of 42 in an 80-row live sample had two).
    /// </remarks>
    public static IReadOnlyList<LinkPreview> SuccessfulLinks(this LinkMetadataEnvelope? envelope) =>
        envelope?.Links?.Where(l => l.IsRenderable()).ToList() ?? new List<LinkPreview>();

    /// <summary>True when this one unfurl produced something to show.</summary>
    public static bool IsRenderable(this LinkPreview? link) =>
        link is not null && link.FetchStatus is null or "success" && link.Metadata is not null;
}
