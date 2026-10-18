using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

public static class SpreadEventKeys
{
    public const string MessageType = "TradeOpportunityModel";

    /// <summary>Kafka key for any Open/Update/Close event of this opportunity. Uses the stable A/B exchanges,
    /// never the long/short legs, which flip with the sign of the spread.</summary>
    public static string For(TradeOpportunityModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var a = model.ExchangeRateA ?? throw new ArgumentException("ExchangeRateA is required to build a spread key.", nameof(model));
        var b = model.ExchangeRateB ?? throw new ArgumentException("ExchangeRateB is required to build a spread key.", nameof(model));

        return SpreadKey.Build(ToKeyType(model.Type), a.Exchange, b.Exchange, a.Symbol);
    }

    private static SpreadKeyType ToKeyType(SpreadType type) => type switch
    {
        SpreadType.Futures => SpreadKeyType.Futures,
        SpreadType.Funding => SpreadKeyType.Funding,
        SpreadType.Spot => SpreadKeyType.Spot,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown spread type."),
    };
}
