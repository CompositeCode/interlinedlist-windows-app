using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// What a creating materialize (<see cref="MaterializeTarget.List"/>,
/// <see cref="MaterializeTarget.Doc"/>, <see cref="MaterializeTarget.Both"/>)
/// returns: <c>201</c> with <c>{ list?, document? }</c> — <see cref="List"/> when a
/// list was created, <see cref="Document"/> when a document was, both for a
/// <c>both</c> target.
/// <para>
/// <b>Unverified envelope</b> — see <see cref="MaterializeCreatedRef"/>. Parsed
/// leniently (missing/renamed keys leave the property null rather than throwing)
/// and <see cref="Raw"/> keeps the whole response so nothing is silently lost.
/// </para>
/// <see cref="MaterializeTarget.Message"/> does not come back here at all — it
/// returns a <see cref="MessageDraft"/>, which is a genuinely different result.
/// </summary>
public sealed class MaterializeResult
{
    public MaterializeCreatedRef? List { get; init; }
    public MaterializeCreatedRef? Document { get; init; }

    /// <summary>The untouched response envelope, for diagnostics until the shape is confirmed live.</summary>
    public JsonElement Raw { get; init; }

    public bool CreatedList => List is not null;
    public bool CreatedDocument => Document is not null;
}
