namespace ArbitrageScanner.Tests.Helpers;

internal static class OrderBookBuilder
{
    internal static ccxt.OrderBook Build(
        IEnumerable<(double price, double volume)>? asks = null,
        IEnumerable<(double price, double volume)>? bids = null)
    {
        return new ccxt.OrderBook
        {
            asks = asks?.Select(e => new List<double> { e.price, e.volume }).ToList()
                   ?? new List<List<double>>(),
            bids = bids?.Select(e => new List<double> { e.price, e.volume }).ToList()
                   ?? new List<List<double>>()
        };
    }
}
