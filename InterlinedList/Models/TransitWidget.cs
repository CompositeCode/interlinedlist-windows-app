namespace InterlinedList.Models;

/// <summary>
/// GET /api/widgets/transit and GET /api/widgets/transit/stops — both return
/// this same envelope.
///
/// <para><b>The `agency` query parameter is mandatory and undocumented.</b> The
/// OpenAPI spec declares *no* parameters for either path, yet a bare call
/// returns <c>400 {"error":"Unknown agency","code":"bad_request"}</c>. The name
/// was recovered by probing live 2026-09-16: of
/// agency/agencyId/agency_id/system/provider/feed/region/city/id/operator/
/// network/source/org, only <c>agency</c> is read, and its vocabulary is
/// *city slugs*, not transit-operator names — <c>seattle</c> and
/// <c>portland</c> both returned 200, while sound-transit, king-county-metro,
/// nyc, chicago, boston, san-francisco, washington, los-angeles, denver,
/// toronto, london, bart, trimet (and 15 other candidates) all returned
/// 400 "Unknown agency". <c>lat</c>/<c>lon</c>/<c>latitude</c>/<c>longitude</c>
/// /<c>q</c>/<c>stopId</c> made no difference to the response once a valid
/// agency was supplied.</para>
///
/// <para><b>The upstream is currently down.</b> Every accepted agency answered
/// <c>200 {"agency":"seattle","stops":[],"error":"unavailable"}</c> — a
/// non-empty <see cref="Error"/> with an empty <see cref="Stops"/> is the
/// server telling us its third-party transit feed failed. Treat a populated
/// <see cref="Error"/> as "show the quiet placeholder", not as a client bug.
/// Because of that outage the *element* shape of <see cref="Stops"/> could not
/// be observed — see <see cref="TransitStop"/>.</para>
/// </summary>
public sealed class TransitWidget
{
    public string? Agency { get; init; }
    public List<TransitStop> Stops { get; init; } = new();

    /// <summary>
    /// Present (e.g. "unavailable") when the server's upstream transit feed
    /// failed. The HTTP status is still 200 in that case, so this is the only
    /// signal — a widget must check it rather than trusting the status code.
    /// </summary>
    public string? Error { get; init; }
}
