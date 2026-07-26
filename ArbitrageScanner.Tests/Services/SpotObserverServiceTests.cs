using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Spot.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class SpotObserverServiceTests
{
    private const string FuturesSymbol = "BTC/USDT:USDT";
    private const string SpotSymbol = "BTC/USDT";

    private static ExchangeRateModel Rate(string exchange, string symbol, double rate) => new()
    {
        Symbol = symbol,
        Exchange = exchange,
        ExchangeRate = rate,
    };

    private static TradeOpportunityModel Opportunity() => new()
    {
        Guid = Guid.NewGuid(),
        Symbol = SpotSymbol,
        Type = SpreadType.Spot,
        Spread = 1.0,
        ExchangeRateA = Rate("binance-spot-obs", SpotSymbol, 100),
        ExchangeRateB = Rate("okx-spot-obs", FuturesSymbol, 110),
        ExchangeLong = Rate("binance-spot-obs", SpotSymbol, 100),
        ExchangeShort = Rate("okx-spot-obs", FuturesSymbol, 110),
    };

    private static (SpotObserverService service, Mock<IServicesCommunicationService> comms, Mock<ITelegramNotifierService> telegram, DataService dataService)
        BuildObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? ServiceFactory.BuildConfig();
        var ds = dataService ?? ServiceFactory.BuildDataService(cfg);
        var calculator = new SpotPositionCalculatorService(cfg, ds);
        var comms = new Mock<IServicesCommunicationService>();
        comms.Setup(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>())).Returns(Task.CompletedTask);
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var ui = new UserInterfaceService(cfg, ds, telegram.Object);
        var service = new SpotObserverService(ds, cfg, calculator, comms.Object, ui);
        return (service, comms, telegram, ds);
    }

    private static void ConfigureFakeExchange(ExchangeService exchangeService, double price)
    {
        var fake = (FakeExchange)exchangeService.GetExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, price));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (price - 0.1, 1000.0) }, asks: new[] { (price + 0.1, 1000.0) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_NewSpotOpportunity_AddsToWatchListAndPostsEverywhere()
    {
        var (service, comms, telegram, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListSpot.TryRemove(combineKey, out _);

        await service.CheckAndAddNewFuturesPositionsToWatch(new List<TradeOpportunityModel> { opportunity });

        dataService.WatchListSpot.Should().ContainKey(combineKey);
        dataService.WatchListSpot[combineKey].Type.Should().Be(SpreadType.Spot);
        comms.Verify(c => c.PostPossiblePosition(opportunity), Times.Once);
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_AlreadyWatched_SkipsDuplicate()
    {
        var (service, comms, _, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListSpot[combineKey] = opportunity;

        await service.CheckAndAddNewFuturesPositionsToWatch(new List<TradeOpportunityModel> { Opportunity() });

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_EmptyList_DoesNothing()
    {
        var (service, comms, _, _) = BuildObserver();

        await service.CheckAndAddNewFuturesPositionsToWatch(new List<TradeOpportunityModel>());

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Never);
    }

    [Fact]
    public async Task WatchPossibleSpotPositionWithCombineKey_SpreadStillWide_UpdatesWatchList()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-spot-obs", FuturesSymbol, SpotSymbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-spot-obs", FuturesSymbol, SpotSymbol);
        ConfigureFakeExchange(binance, 100);
        ConfigureFakeExchange(okx, 110);

        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);

        await service.WatchPossibleSpotPositionWithCombineKey(opportunity);

        // WatchListSpot is process-wide static state shared with other test classes running in
        // parallel, so only assert on this test's own key rather than the dictionary's total size.
        dataService.WatchListSpot.Should().ContainKey(combineKey);
        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Once);
    }

    [Fact]
    public async Task WatchPossibleSpotPositionWithCombineKey_SpreadClosed_RemovesAndNotifies()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-spot-obs", FuturesSymbol, SpotSymbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-spot-obs", FuturesSymbol, SpotSymbol);
        ConfigureFakeExchange(binance, 100);
        ConfigureFakeExchange(okx, 100.01);

        var (service, _, telegram, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListSpot[combineKey] = opportunity;

        await service.WatchPossibleSpotPositionWithCombineKey(opportunity);

        dataService.WatchListSpot.Should().NotContainKey(combineKey);
        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spread closed"))), Times.Once);
    }

    [Fact]
    public async Task WatchPossibleSpotPositionWithCombineKey_CalculatorThrows_LogsAndDoesNotPropagate()
    {
        var (service, comms, _, _) = BuildObserver();
        var opportunity = Opportunity();

        var act = async () => await service.WatchPossibleSpotPositionWithCombineKey(opportunity);

        await act.Should().NotThrowAsync();
        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Never);
    }

    [Fact]
    public async Task StartToWatchPositionsWithCombineKeys_PreCancelledToken_ReturnsImmediately()
    {
        var (service, _, _, _) = BuildObserver();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.StartToWatchPositionsWithCombineKeys(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartToWatchPositionsWithCombineKeys_QueuedItem_DispatchesItForWatching()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-spot-obs", FuturesSymbol, SpotSymbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-spot-obs", FuturesSymbol, SpotSymbol);
        ConfigureFakeExchange(binance, 100);
        ConfigureFakeExchange(okx, 110);
        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListSpot[combineKey] = opportunity;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartToWatchPositionsWithCombineKeys(cts.Token);
        await Task.Delay(300);

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.AtLeastOnce);
    }
}
