using System.Collections.ObjectModel;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// GET /api/widgets/markets — the server's default crypto watchlist
/// (BTC/ETH/SOL as observed). Called without <c>ids</c> on purpose: a real
/// three-id request returned 502, so the default list is the reliable path.
/// </summary>
public sealed partial class MarketsWidgetViewModel : WidgetViewModel
{
    // Quotes move, but this proxies CoinGecko — two minutes is plenty for a
    // sidebar and keeps well clear of upstream rate limits.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public MarketsWidgetViewModel(SessionService session) : base(session) { }

    public override string Title => "Markets";

    protected override TimeSpan CacheFor => Ttl;

    public ObservableCollection<MarketQuoteViewModel> Quotes { get; } = new();

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        var widget = await Session.Api.GetMarketsWidgetAsync(ct: ct);

        Quotes.Clear();
        foreach (var quote in widget.Quotes)
            Quotes.Add(new MarketQuoteViewModel(quote));

        if (Quotes.Count == 0)
            ShowPlaceholder("No quotes right now.");
        else
            ShowContent();
    }
}
