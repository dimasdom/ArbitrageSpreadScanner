using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class FundingPositionCalculatorServiceTests
{
    [Fact]
    public void CalculateSlippage_EmptyOrderBook_Throws()
    {
        var book = OrderBookBuilder.Build(asks: [], bids: []);
        var act = () => FundingPositionCalculatorService.CalculateSlippage(book, "BTC/USDT:USDT", orderSize: 10.0, isLong: true);
        act.Should().Throw<Exception>().WithMessage("Order book is empty!");
    }

    [Fact]
    public void CalculateSlippage_InsufficientLiquidity_Throws()
    {
        var book = OrderBookBuilder.Build(asks: [(100.0, 3.0)], bids: []);
        var act = () => FundingPositionCalculatorService.CalculateSlippage(book, "BTC/USDT:USDT", orderSize: 10.0, isLong: true);
        act.Should().Throw<Exception>().WithMessage("Not Enough Liqudidy!");
    }

    [Fact]
    public async Task FindPossiblePositionsForExchangeSet_FundingBelowThreshold_ReturnsNoOpportunities()
    {
        var config = ServiceFactory.BuildConfig(fundingThreshold: 0.01);
        var svc = ServiceFactory.BuildFundingCalculator(config);
        const string symbol = "BTC/USDT:USDT";
        var coinData = new CoinDataModel
        {
            Symbol = symbol,
            ExchangeRates = new List<ExchangeRateModel>
            {
                new() { Symbol = symbol, Exchange = "binance", FundingRate = new ccxt.FundingRate { fundingRate = 0.001 } },
                new() { Symbol = symbol, Exchange = "okx", FundingRate = new ccxt.FundingRate { fundingRate = 0.001 } },
            },
        };

        var result = await svc.FindPossiblePositionsForExchangeSet(coinData);

        result.Should().BeEmpty();
    }
}
