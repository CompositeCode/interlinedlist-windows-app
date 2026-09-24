namespace InterlinedList.Models;

/// <summary>
/// One quote from GET /api/widgets/markets. <c>price</c> came back as both an
/// integer (76226) and a fraction (2411.94) across observed rows, so it is
/// typed <see cref="double"/> rather than a narrower numeric type.
/// <c>change24h</c> is a percentage.
/// </summary>
public sealed class MarketQuote
{
    public string? Symbol { get; init; }
    public double? Price { get; init; }
    public double? Change24h { get; init; }
}
