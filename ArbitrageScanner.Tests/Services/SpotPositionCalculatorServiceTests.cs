using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Spot.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class SpotPositionCalculatorServiceTests
{
    private const string FuturesSymbol = "BTC/USDT:USDT";
    private const string SpotSymbol = "BTC/USDT";

    private static ExchangeRateModel FuturesRate(string exchange, double rate, ExchangeRateModel? spotTicker = null) => new()
    {
        Symbol = FuturesSymbol,
        Exchange = exchange,
        ExchangeRate = rate,
        structOrderBook = OrderBookBuilder.Build(bids: new[] { (rate - 0.1, 1000.0) }, asks: new[] { (rate + 0.1, 1000.0) }),
        SpotTicker = spotTicker,
    };

    private static ExchangeRateModel SpotTicker(string exchange, double rate) => new()
    {
        Symbol = SpotSymbol,
        Exchange = exchange,
        ExchangeRate = rate,
        structOrderBook = OrderBookBuilder.Build(bids: new[] { (rate - 0.1, 1000.0) }, asks: new[] { (rate + 0.1, 1000.0) }),
    };

    [Theory]
    [InlineData(110, 100, 10)]
    [InlineData(90, 100, -10)]
    public void CalculateBasis_ComputesPercentDifference(double markPrice, double indexPrice, double expected)
    {
        var calc = new SpotPositionCalculatorService(ServiceFactory.BuildConfig(), ServiceFactory.BuildDataService());

        calc.CalculateBasis(markPrice, indexPrice).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void CalculateSpreadFor_ComputesPercentDifference()
    {
        var calc = new SpotPositionCalculatorService(ServiceFactory.BuildConfig(), ServiceFactory.BuildDataService());

        calc.CalculateSpreadFor(110, 100).Should().BeApproximately(10, 1e-9);
    }

    [Fact]
    public async Task FindPossiblePositionsForExchangeSet_NoSpotTickers_ReturnsEmpty()
    {
        var config = ServiceFactory.BuildConfigWithFlags(spreadSize: 0.1);
        var dataService = ServiceFactory.BuildDataService(config);
        var calc = new SpotPositionCalculatorService(config, dataService);
        var coinData = new CoinDataModel
        {
            Symbol = FuturesSymbol,
            ExchangeRates = new List<ExchangeRateModel> { FuturesRate("binance", 100), FuturesRate("okx", 101) },
        };

        var result = await calc.FindPossiblePositionsForExchangeSet(coinData);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossiblePositionsForExchangeSet_SpreadBelowThreshold_ReturnsEmpty()
    {
        var config = ServiceFactory.BuildConfigWithFlags(spreadSize: 50, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance", FuturesSymbol, SpotSymbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx", FuturesSymbol, SpotSymbol);
        var calc = new SpotPositionCalculatorService(config, dataService);
        var coinData = new CoinDataModel
        {
            Symbol = FuturesSymbol,
            ExchangeRates = new List<ExchangeRateModel>
            {
                FuturesRate("binance", 100, SpotTicker("binance", 100)),
                FuturesRate("okx", 100.5, SpotTicker("okx", 100.5)),
            },
        };

        var result = await calc.FindPossiblePositionsForExchangeSet(coinData);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossiblePositionsForExchangeSet_FuturesAboveSpotBeyondThreshold_ReturnsOpportunity()
    {
        var config = ServiceFactory.BuildConfigWithFlags(spreadSize: 0.5, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance", FuturesSymbol, SpotSymbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx", FuturesSymbol, SpotSymbol);
        var calc = new SpotPositionCalculatorService(config, dataService);
        var coinData = new CoinDataModel
        {
            Symbol = FuturesSymbol,
            ExchangeRates = new List<ExchangeRateModel>
            {
                FuturesRate("binance", 110, SpotTicker("binance", 100)),
                FuturesRate("okx", 100, SpotTicker("okx", 100)),
            },
        };

        var result = await calc.FindPossiblePositionsForExchangeSet(coinData);

        result.Should().ContainSingle();
        result[0].ExchangeShort!.Exchange.Should().Be("binance");
        result[0].ExchangeLong!.Exchange.Should().Be("okx");
        result[0].Symbol.Should().Be(SpotSymbol);
    }

    [Fact]
    public void CalculateSlippage_EmptyOrderBook_Throws()
    {
        var orderBook = OrderBookBuilder.Build();

        var act = () => SpotPositionCalculatorService.CalculateSlippage(orderBook, SpotSymbol, 1, true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
    }

    [Fact]
    public void CalculateSlippage_InsufficientLiquidity_Throws()
    {
        var orderBook = OrderBookBuilder.Build(asks: new[] { (100.0, 0.1) });

        var act = () => SpotPositionCalculatorService.CalculateSlippage(orderBook, SpotSymbol, 10, true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Liqudidy*");
    }

    [Fact]
    public void CalculateSlippage_SufficientLiquidity_ComputesWeightedAverage()
    {
        var orderBook = OrderBookBuilder.Build(asks: new[] { (100.0, 5.0), (101.0, 5.0) });

        var slippage = SpotPositionCalculatorService.CalculateSlippage(orderBook, SpotSymbol, 10, true);

        slippage.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public async Task WatchPossiblePosition_MissingExchangeRates_ReturnsNull()
    {
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance", FuturesSymbol, SpotSymbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx", FuturesSymbol, SpotSymbol);
        var binanceFake = (FakeExchange)dataService.ExchangeObserverServices["binance"].GetExchange();
        binanceFake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, null));
        binanceFake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook());
        var okxFake = (FakeExchange)dataService.ExchangeObserverServices["okx"].GetExchange();
        okxFake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        okxFake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(bids: new[] { (99.9, 1000.0) }, asks: new[] { (100.1, 1000.0) }));

        var calc = new SpotPositionCalculatorService(config, dataService);
        var opportunity = new TradeOpportunityModel
        {
            ExchangeLong = SpotTicker("binance", 100),
            ExchangeShort = FuturesRate("okx", 100),
        };

        var result = await calc.WatchPossiblePosition(opportunity);

        result.Should().BeNull();
    }
}
