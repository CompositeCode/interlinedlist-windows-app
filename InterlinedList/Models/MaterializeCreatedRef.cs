namespace InterlinedList.Models;

/// <summary>
/// A pointer to something POST /api/materialize created — documented as
/// <c>{ id, title }</c>.
/// <para>
/// <b>Unverified envelope.</b> The create targets (list / doc / both) were
/// deliberately never exercised live: they write real content, and the only
/// account available is shared test infrastructure. Per this repo's
/// read-after-write rule, treat <see cref="Id"/> as the one thing worth trusting
/// and re-fetch with GetListAsync / GetDocumentAsync instead of relying on
/// anything else here. <see cref="MaterializeResult.Raw"/> keeps the untouched
/// envelope for whoever verifies it first.
/// </para>
/// </summary>
public sealed class MaterializeCreatedRef
{
    public required string Id { get; init; }
    public string? Title { get; init; }
}
