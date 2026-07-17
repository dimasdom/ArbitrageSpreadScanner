using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class SlippageCalculationTests
{
    [Fact]
    public void Long_SingleLevel_ExactFill_ReturnsZero()
    {
        var book = OrderBookBuilder.Build(asks: [(100.0, 10.0)], bids: [(99.0, 10.0)]);
        var slippage = FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: true);
        slippage.Should().Be(0.0);
    }

    [Fact]
    public void Long_TwoLevels_ReturnsPositiveSlippage()
    {
        var book = OrderBookBuilder.Build(asks: [(100.0, 5.0), (101.0, 5.0)], bids: []);
        var slippage = FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: true);
        slippage.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Short_SingleLevel_ExactFill_ReturnsZero()
    {
        var book = OrderBookBuilder.Build(asks: [], bids: [(99.0, 10.0)]);
        var slippage = FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: false);
        slippage.Should().Be(0.0);
    }

    [Fact]
    public void Short_TwoLevels_ReturnsNegativeSlippage()
    {
        var book = OrderBookBuilder.Build(asks: [], bids: [(99.0, 5.0), (98.0, 5.0)]);
        var slippage = FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: false);
        slippage.Should().BeLessThan(0.0);
    }

    [Fact]
    public void PartialFillAtSecondLevel_CorrectWeightedAverage()
    {
        var book = OrderBookBuilder.Build(asks: [(100.0, 5.0), (102.0, 10.0)], bids: []);
        var slippage = FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 8.0, isLong: true);
        slippage.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void EmptyOrderBook_ThrowsException()
    {
        var book = OrderBookBuilder.Build(asks: [], bids: []);
        var act = () => FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: true);
        act.Should().Throw<Exception>().WithMessage("Order book is empty!");
    }

    [Fact]
    public void InsufficientLiquidity_ThrowsException()
    {
        var book = OrderBookBuilder.Build(asks: [(100.0, 3.0)], bids: []);
        var act = () => FuturesPositionCalculatorService.CalculateSlippage(book, "BTC/USDT", orderSize: 10.0, isLong: true);
        act.Should().Throw<Exception>().WithMessage("Not Enough Liqudidy!");
    }
}
