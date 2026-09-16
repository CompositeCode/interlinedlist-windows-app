namespace InterlinedList.Models;

/// <summary>
/// GET /api/widgets/markets — crypto watchlist quotes.
/// Live-verified 2026-09-16: 200 { "quotes": [ { "symbol", "price", "change24h" } ] }
/// — the default watchlist is BTC/ETH/SOL.
///
/// The <c>ids</c> query parameter takes CoinGecko *ids*, not ticker symbols:
/// <c>?ids=bitcoin</c> returned only BTC, while <c>?ids=BTC,ETH</c> and
/// <c>?ids=zzzzzz</c> both fell back to the full default watchlist. A
/// three-id request (<c>?ids=bitcoin,ethereum,dogecoin</c>) returned
/// <c>502 {"error":"Failed to fetch markets","code":"bad_gateway"}</c> — a live
/// demonstration that this endpoint proxies a third party that fails
/// independently of InterlinedList, which is why the widget layer degrades to a
/// quiet placeholder instead of surfacing an error. The client therefore calls
/// this endpoint bare and takes the server's default watchlist.
/// </summary>
public sealed class MarketsWidget
{
    public List<MarketQuote> Quotes { get; init; } = new();
}
