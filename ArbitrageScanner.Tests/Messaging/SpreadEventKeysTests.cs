using Xunit;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using FluentAssertions;

namespace ArbitrageScanner.Tests.Messaging;

public class SpreadEventKeysTests
{
    private static ExchangeRateModel Rate(string exchange, string symbol = "BTC/USDT:USDT") =>
        new() { Symbol = symbol, Exchange = exchange };

    private static TradeOpportunityModel Opportunity(string exchangeA, string exchangeB, SpreadType type, bool aIsLong)
    {
        var a = Rate(exchangeA);
        var b = Rate(exchangeB);
        return new TradeOpportunityModel
        {
            Type = type,
            ExchangeRateA = a,
            ExchangeRateB = b,
            ExchangeLong = aIsLong ? a : b,
            ExchangeShort = aIsLong ? b : a,
        };
    }

    [Fact]
    public void Key_does_not_change_when_the_long_and_short_legs_flip()
    {
        var before = SpreadEventKeys.For(Opportunity("Binance", "Bybit", SpreadType.Futures, aIsLong: true));
        var after = SpreadEventKeys.For(Opportunity("Binance", "Bybit", SpreadType.Futures, aIsLong: false));

        after.Should().Be(before);
    }

    [Fact]
    public void Key_is_the_same_whichever_exchange_is_A()
    {
        var ab = SpreadEventKeys.For(Opportunity("Binance", "Bybit", SpreadType.Futures, aIsLong: true));
        var ba = SpreadEventKeys.For(Opportunity("Bybit", "Binance", SpreadType.Futures, aIsLong: true));

        ba.Should().Be(ab);
        ab.Should().Be("futures:binance:bybit:BTC-USDT.USDT");
    }

    [Theory]
    [InlineData(SpreadType.Futures, "futures")]
    [InlineData(SpreadType.Funding, "funding")]
    [InlineData(SpreadType.Spot, "spot")]
    public void Key_is_prefixed_with_the_spread_type(SpreadType type, string prefix)
    {
        SpreadEventKeys.For(Opportunity("Binance", "Bybit", type, aIsLong: true)).Should().StartWith(prefix + ":");
    }

    [Fact]
    public void Missing_exchange_rates_are_rejected()
    {
        var act = () => SpreadEventKeys.For(new TradeOpportunityModel { ExchangeRateA = Rate("Binance") });

        act.Should().Throw<ArgumentException>();
    }
}
