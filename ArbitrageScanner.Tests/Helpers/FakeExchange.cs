using ccxt;

namespace ArbitrageScanner.Tests.Helpers;

/// <summary>
/// ccxt.Exchange only exposes virtual hooks on the lowercase "raw" fetch methods (returning object);
/// the typed FetchTicker/FetchOrderBook/FetchFundingRate/LoadMarkets wrappers used by ExchangeService
/// are not virtual, so canned responses must be shaped as the raw dictionaries ccxt's parsers expect.
/// </summary>
internal sealed class FakeExchange : Exchange
{
    public Func<object, object?, Task<object>>? TickerProvider;
    public Func<object, object?, object?, Task<object>>? OrderBookProvider;
    public Func<object, object?, Task<object>>? FundingRateProvider;
    public Func<object?, Task<object>>? MarketsProvider;

    public FakeExchange(string name = "FakeExchange")
    {
        this.name = name;
    }

    public override Task<object> fetchTicker(object symbol, object? parameters = null)
    {
        if (TickerProvider is null)
            throw new InvalidOperationException("TickerProvider not configured");
        return TickerProvider(symbol, parameters);
    }

    public override Task<object> fetchOrderBook(object symbol, object? limit = null, object? parameters = null)
    {
        if (OrderBookProvider is null)
            throw new InvalidOperationException("OrderBookProvider not configured");
        return OrderBookProvider(symbol, limit, parameters);
    }

    public override Task<object> fetchFundingRate(object symbol, object? parameters = null)
    {
        if (FundingRateProvider is null)
            throw new InvalidOperationException("FundingRateProvider not configured");
        return FundingRateProvider(symbol, parameters);
    }

    public override Task<object> fetchMarkets(object? parameters = null)
    {
        if (MarketsProvider is null)
            throw new InvalidOperationException("MarketsProvider not configured");
        return MarketsProvider(parameters);
    }

    public static Dictionary<string, object?> Ticker(string symbol, double? last)
        => new()
        {
            ["symbol"] = symbol,
            ["last"] = last,
            ["timestamp"] = 0L,
        };

    public static Dictionary<string, object?> OrderBook(
        IEnumerable<(double price, double volume)>? bids = null,
        IEnumerable<(double price, double volume)>? asks = null)
        => new()
        {
            ["bids"] = (bids ?? Enumerable.Empty<(double, double)>())
                .Select(b => new List<object> { b.price, b.volume }).Cast<object>().ToList(),
            ["asks"] = (asks ?? Enumerable.Empty<(double, double)>())
                .Select(a => new List<object> { a.price, a.volume }).Cast<object>().ToList(),
            ["timestamp"] = 0L,
        };

    public static Dictionary<string, object?> FundingRate(string symbol, double? rate)
        => new()
        {
            ["symbol"] = symbol,
            ["fundingRate"] = rate,
            ["timestamp"] = 0L,
        };

    public static Dictionary<string, object?> Market(string symbol, bool swap, bool spot, string? id = null)
    {
        var parts = symbol.Split(':')[0].Split('/');
        var bas = parts[0];
        var quote = parts.Length > 1 ? parts[1] : "USDT";
        object? linear = null;
        object? inverse = null;
        if (swap)
        {
            linear = true;
            inverse = false;
        }
        return new()
        {
            ["id"] = id ?? symbol.Replace("/", "").Replace(":", ""),
            ["symbol"] = symbol,
            ["base"] = bas,
            ["quote"] = quote,
            ["settle"] = swap ? quote : null,
            ["baseId"] = bas,
            ["quoteId"] = quote,
            ["settleId"] = swap ? quote : null,
            ["type"] = swap ? "swap" : "spot",
            ["spot"] = spot,
            ["margin"] = false,
            ["swap"] = swap,
            ["future"] = false,
            ["option"] = false,
            ["active"] = true,
            ["contract"] = swap,
            ["linear"] = linear,
            ["inverse"] = inverse,
            ["taker"] = 0.001,
            ["maker"] = 0.001,
            ["contractSize"] = swap ? 1.0 : (object?)null,
            ["expiry"] = null,
            ["expiryDatetime"] = null,
            ["strike"] = null,
            ["optionType"] = null,
            ["precision"] = new Dictionary<string, object?> { ["amount"] = 0.0001, ["price"] = 0.01 },
            ["limits"] = new Dictionary<string, object?>
            {
                ["amount"] = new Dictionary<string, object?> { ["min"] = 0.0001, ["max"] = null },
                ["price"] = new Dictionary<string, object?> { ["min"] = null, ["max"] = null },
                ["cost"] = new Dictionary<string, object?> { ["min"] = null, ["max"] = null },
            },
            ["created"] = null,
            ["info"] = new Dictionary<string, object?>(),
        };
    }
}
