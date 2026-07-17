using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.IntegrationTests.Support;

internal static class TradeOpportunityModelBuilder
{
    public static TradeOpportunityModel Build(
        Guid? guid = null,
        SpreadType type = SpreadType.Futures,
        OrderStatus actionType = OrderStatus.Open,
        string symbol = "BTC/USDT",
        double spread = 2.25,
        double possibleProfit = 15)
    {
        ExchangeRateModel Rate(string exchange) => new()
        {
            Symbol = symbol,
            Exchange = exchange,
            ExchangeRate = 50_000,
            VolumeAsk = 100,
            VolumeBid = 100,
            SlippageLong = 0.1,
            SlippageShort = 0.2
        };

        return new TradeOpportunityModel
        {
            Guid = guid ?? Guid.NewGuid(),
            Symbol = symbol,
            Type = type,
            ActionType = actionType,
            ExchangeRateA = Rate("Binance"),
            ExchangeRateB = Rate("Bybit"),
            ExchangeShort = Rate("Binance"),
            ExchangeLong = Rate("Bybit"),
            Spread = spread,
            StartSpread = spread,
            SummaryTarrif = 0.1,
            PossibleProfit = possibleProfit,
            TotalFunding = 0.05,
            DateTime = DateTime.UtcNow
        };
    }
}
