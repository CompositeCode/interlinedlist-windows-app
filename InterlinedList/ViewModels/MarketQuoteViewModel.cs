using System.Globalization;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// One row of the Markets widget, pre-formatted for display so the XAML stays
/// free of converters.
/// </summary>
public sealed class MarketQuoteViewModel
{
    public MarketQuoteViewModel(MarketQuote quote)
    {
        Symbol = quote.Symbol ?? "—";
        Price = FormatPrice(quote.Price);
        Change = FormatChange(quote.Change24h);
        IsDown = quote.Change24h < 0;
    }

    public string Symbol { get; }
    public string Price { get; }
    public string Change { get; }

    /// <summary>Drives the colour of the change figure (amber down, green up).</summary>
    public bool IsDown { get; }

    // Observed prices spanned 98.41 to 76226, so switch precision by magnitude
    // rather than showing "$76,226.00" next to "$98.41".
    private static string FormatPrice(double? price) => price switch
    {
        null => "—",
        >= 1000 => "$" + price.Value.ToString("N0", CultureInfo.CurrentCulture),
        _ => "$" + price.Value.ToString("N2", CultureInfo.CurrentCulture),
    };

    private static string FormatChange(double? change)
        => change is null
            ? ""
            : (change >= 0 ? "+" : "") + change.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%";
}
