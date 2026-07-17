using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class FormatOrdersToSendTests
{
    [Fact]
    public void BothExchangeRates_HaveOrderBooks_PopulatesAllFourLists()
    {
        var model = BuildModel(
            aAsks: [(100.0, 1.0)], aBids: [(99.0, 1.0)],
            bAsks: [(200.0, 2.0)], bBids: [(199.0, 2.0)]);

        model.FormatOrdersToSend();

        model.AsksExchangeA.Should().NotBeNull().And.HaveCount(1);
        model.BidsExchangeA.Should().NotBeNull().And.HaveCount(1);
        model.AsksExchangeB.Should().NotBeNull().And.HaveCount(1);
        model.BidsExchangeB.Should().NotBeNull().And.HaveCount(1);
    }

    [Fact]
    public void MapsCorrectPriceAndAmount()
    {
        var model = BuildModel(
            aAsks: [(42000.5, 1.5)], aBids: [(41999.0, 0.8)],
            bAsks: [(42010.0, 0.3)], bBids: [(42005.0, 1.0)]);

        model.FormatOrdersToSend();

        model.AsksExchangeA![0].Price.Should().Be(42000.5);
        model.AsksExchangeA[0].Amount.Should().Be(1.5);
        model.BidsExchangeA![0].Price.Should().Be(41999.0);
        model.BidsExchangeA[0].Amount.Should().Be(0.8);
    }

    [Fact]
    public void NullExchangeRateA_LeavesListsANull()
    {
        var model = new TradeOpportunityModel
        {
            ExchangeRateA = null,
            ExchangeRateB = BuildRate(
                asks: [(100.0, 1.0)], bids: [(99.0, 1.0)])
        };

        model.FormatOrdersToSend();

        model.AsksExchangeA.Should().BeNull();
        model.BidsExchangeA.Should().BeNull();
        model.AsksExchangeB.Should().NotBeNull();
    }

    [Fact]
    public void MultipleOrderBookLevels_AllMapped()
    {
        var asks = new[] { (100.0, 5.0), (101.0, 3.0), (102.0, 2.0) };
        var bids = new[] { (99.0, 5.0), (98.0, 3.0) };
        var model = BuildModel(aAsks: asks, aBids: bids, bAsks: asks, bBids: bids);

        model.FormatOrdersToSend();

        model.AsksExchangeA.Should().HaveCount(3);
        model.BidsExchangeA.Should().HaveCount(2);
    }

    private static TradeOpportunityModel BuildModel(
        IEnumerable<(double, double)> aAsks, IEnumerable<(double, double)> aBids,
        IEnumerable<(double, double)> bAsks, IEnumerable<(double, double)> bBids) =>
        new()
        {
            ExchangeRateA = BuildRate(aAsks, aBids),
            ExchangeRateB = BuildRate(bAsks, bBids)
        };

    private static ExchangeRateModel BuildRate(
        IEnumerable<(double price, double volume)> asks,
        IEnumerable<(double price, double volume)> bids) =>
        new()
        {
            Symbol = "BTC/USDT:USDT",
            Exchange = "binance",
            structOrderBook = OrderBookBuilder.Build(asks, bids)
        };
}
