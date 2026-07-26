using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class ExchangePairServiceTests
{
    private static ccxt.pro.OrderBook BuildDynamicOrderBook(
        IEnumerable<(double price, double volume)> bids,
        IEnumerable<(double price, double volume)> asks)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["bids"] = bids.Select(b => new List<object> { b.price, b.volume }).Cast<object>().ToList(),
            ["asks"] = asks.Select(a => new List<object> { a.price, a.volume }).Cast<object>().ToList(),
            ["timestamp"] = 0L,
            ["nonce"] = null,
        };
        return new ccxt.pro.OrderBook(snapshot);
    }

    [Fact]
    public void GetLiquidity_DynamicOrderBook_SumsOnlyLevelsWithinHalfPercentBand()
    {
        var orderBook = BuildDynamicOrderBook(
            bids: new[] { (99.9, 10.0), (90.0, 100.0) },
            asks: new[] { (100.1, 10.0), (110.0, 100.0) });

        var (bidLiquid, askLiquid) = ExchangePairService.GetLiquidity(orderBook, 100);

        bidLiquid.Should().Be(99.9 * 10.0);
        askLiquid.Should().Be(100.1 * 10.0);
    }

    [Fact]
    public void GetLiquidity_StructOrderBook_SumsOnlyLevelsWithinHalfPercentBand()
    {
        var orderBook = OrderBookBuilder.Build(
            bids: new[] { (99.9, 10.0), (90.0, 100.0) },
            asks: new[] { (100.1, 10.0), (110.0, 100.0) });

        var (bidLiquid, askLiquid) = ExchangePairService.GetLiquidity(orderBook, 100);

        bidLiquid.Should().Be(99.9 * 10.0);
        askLiquid.Should().Be(100.1 * 10.0);
    }

    [Fact]
    public void GetLiquidity_StructOrderBook_EmptyLevels_ReturnsZero()
    {
        var orderBook = OrderBookBuilder.Build();

        var (bidLiquid, askLiquid) = ExchangePairService.GetLiquidity(orderBook, 100);

        bidLiquid.Should().Be(0);
        askLiquid.Should().Be(0);
    }

    [Theory]
    [InlineData(10.27, 0.1, 10.2)]
    [InlineData(10.0, 1, 10.0)]
    [InlineData(9.99, 0.01, 9.99)]
    [InlineData(0, 0.5, 0)]
    public void RoundToStep_RoundsDownToNearestStep(double value, double step, double expected)
    {
        var result = ExchangePairService.RoundToStep(value, step);

        result.Should().BeApproximately(expected, 1e-9);
    }
}
