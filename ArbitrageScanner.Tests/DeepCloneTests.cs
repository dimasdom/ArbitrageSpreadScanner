using ArbitrageScanner.Domain.Models;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class DeepCloneTests
{
    [Fact]
    public void Clone_IsNotSameReference()
    {
        var original = BuildCoin();
        var clone = CoinDataModel.DeepClone(original);
        ReferenceEquals(original, clone).Should().BeFalse();
    }

    [Fact]
    public void Clone_HasEqualScalarValues()
    {
        var original = BuildCoin();
        var clone = CoinDataModel.DeepClone(original);
        clone.Symbol.Should().Be(original.Symbol);
        clone.ExchangeRates[0].ExchangeRate.Should().Be(original.ExchangeRates[0].ExchangeRate);
        clone.ExchangeRates[0].Exchange.Should().Be(original.ExchangeRates[0].Exchange);
    }

    [Fact]
    public void ModifyClone_DoesNotAffectOriginal()
    {
        var original = BuildCoin();
        var clone = CoinDataModel.DeepClone(original);

        clone.ExchangeRates[0].ExchangeRate = 99999.0;
        clone.ExchangeRates.Add(new ExchangeRateModel { Symbol = "ETH/USDT", Exchange = "bybit", ExchangeRate = 1.0 });

        original.ExchangeRates.Should().HaveCount(2);
        original.ExchangeRates[0].ExchangeRate.Should().Be(30000.0);
    }

    [Fact]
    public void Clone_NestedExchangeRateList_IsIndependent()
    {
        var original = BuildCoin();
        var clone = CoinDataModel.DeepClone(original);

        clone.ExchangeRates.Clear();

        original.ExchangeRates.Should().HaveCount(2);
    }

    private static CoinDataModel BuildCoin() => new()
    {
        Symbol = "BTC/USDT",
        ExchangeRates = new List<ExchangeRateModel>
        {
            new() { Symbol = "BTC/USDT:USDT", Exchange = "binance", ExchangeRate = 30000.0 },
            new() { Symbol = "BTC/USDT:USDT", Exchange = "okx",     ExchangeRate = 30050.0 }
        }
    };
}
