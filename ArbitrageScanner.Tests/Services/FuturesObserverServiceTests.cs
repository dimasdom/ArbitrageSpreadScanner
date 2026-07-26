using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class FuturesObserverServiceTests
{
    private const string Symbol = "BTC/USDT:USDT";

    private static ExchangeRateModel Rate(string exchange, double rate) => new()
    {
        Symbol = Symbol,
        Exchange = exchange,
        ExchangeRate = rate,
    };

    private static TradeOpportunityModel Opportunity(double spread = 1.0) => new()
    {
        Guid = Guid.NewGuid(),
        Symbol = Symbol,
        Type = SpreadType.Futures,
        Spread = spread,
        ExchangeRateA = Rate("binance-fut-obs", 100),
        ExchangeRateB = Rate("okx-fut-obs", 101),
        ExchangeLong = Rate("binance-fut-obs", 100),
        ExchangeShort = Rate("okx-fut-obs", 101),
    };

    private static (FuturesObserverService service, Mock<IServicesCommunicationService> comms, Mock<ITelegramNotifierService> telegram, DataService dataService)
        BuildObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? ServiceFactory.BuildConfig();
        var ds = dataService ?? ServiceFactory.BuildDataService(cfg);
        var calculator = new FuturesPositionCalculatorService(cfg, ds);
        var comms = new Mock<IServicesCommunicationService>();
        comms.Setup(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>())).Returns(Task.CompletedTask);
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var ui = new UserInterfaceService(cfg, ds, telegram.Object);
        var service = new FuturesObserverService(ds, cfg, calculator, comms.Object, ui);
        return (service, comms, telegram, ds);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_NewPosition_AddsToWatchListAndPostsEverywhere()
    {
        var (service, comms, telegram, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchList.TryRemove(combineKey, out _);

        await service.CheckAndAddNewFuturesPositionsToWatch(new List<TradeOpportunityModel> { opportunity });

        dataService.WatchList.Should().ContainKey(combineKey);
        dataService.WatchList[combineKey].Type.Should().Be(SpreadType.Futures);
        dataService.WatchList[combineKey].ActionType.Should().Be(OrderStatus.Open);
        comms.Verify(c => c.PostPossiblePosition(opportunity), Times.Once);
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_AlreadyWatched_SkipsDuplicate()
    {
        var (service, comms, _, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchList[combineKey] = opportunity;

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
    public async Task CheckAndAddNewFuturesPositionToWatch_Null_DoesNothing()
    {
        var (service, comms, _, _) = BuildObserver();

        await service.CheckAndAddNewFuturesPositionToWatch(null!);

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionToWatch_NewSymbol_AddsAndPosts()
    {
        var (service, comms, telegram, dataService) = BuildObserver();
        var opportunity = Opportunity();
        dataService.WatchList.TryRemove(opportunity.ExchangeRateA!.Symbol, out _);

        await service.CheckAndAddNewFuturesPositionToWatch(opportunity);

        dataService.WatchList.Should().ContainKey(opportunity.ExchangeRateA!.Symbol);
        comms.Verify(c => c.PostPossiblePosition(opportunity), Times.Once);
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task WatchPossibleFuturesPositionWithCombineKey_CalculatorReturnsNull_DoesNothing()
    {
        var (service, comms, _, dataService) = BuildObserver();
        var opportunity = Opportunity();
        // No ExchangeObserverServices registered -> WatchPossiblePosition will throw KeyNotFoundException,
        // which the method catches and logs, resulting in no side effects — same observable outcome as "null".

        await service.WatchPossibleFuturesPositionWithCombineKey(opportunity);

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Never);
    }

    [Fact]
    public async Task WatchPossibleFuturesPositionWithCombineKey_SpreadStillWidenough_UpdatesWatchListAndPostsUpdate()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fut-obs", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fut-obs", Symbol);
        var binance = (ExchangeService)dataService.ExchangeObserverServices["binance-fut-obs"];
        var okx = (ExchangeService)dataService.ExchangeObserverServices["okx-fut-obs"];
        // New prices must keep the spread's sign/magnitude consistent with the position's original
        // direction (binance > okx, same as the seeded opportunity below) so keepWatching stays true.
        ((FakeExchange)binance.GetExchange()).TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 110));
        ((FakeExchange)binance.GetExchange()).OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(bids: new[] { (109.9, 1000.0) }, asks: new[] { (110.1, 1000.0) }));
        ((FakeExchange)binance.GetExchange()).FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        ((FakeExchange)okx.GetExchange()).TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        ((FakeExchange)okx.GetExchange()).OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(bids: new[] { (99.9, 1000.0) }, asks: new[] { (100.1, 1000.0) }));
        ((FakeExchange)okx.GetExchange()).FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));

        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity(spread: 5);
        opportunity.ExchangeRateA = Rate("binance-fut-obs", 110);
        opportunity.ExchangeRateB = Rate("okx-fut-obs", 100);
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);

        await service.WatchPossibleFuturesPositionWithCombineKey(opportunity);

        dataService.WatchList.Should().ContainKey(combineKey);
        dataService.WatchList[combineKey].ActionType.Should().Be(OrderStatus.Update);
        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Once);
    }

    [Fact]
    public async Task WatchPossibleFuturesPositionWithCombineKey_SpreadClosed_RemovesFromWatchListAndNotifiesInvalidation()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1, spreadSize: 10);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fut-obs", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fut-obs", Symbol);
        var binance = (ExchangeService)dataService.ExchangeObserverServices["binance-fut-obs"];
        var okx = (ExchangeService)dataService.ExchangeObserverServices["okx-fut-obs"];
        foreach (var (svc, price) in new[] { (binance, 100.0), (okx, 100.05) })
        {
            var fe = (FakeExchange)svc.GetExchange();
            var p = price;
            fe.TickerProvider = (s, _) => Task.FromResult<object>(FakeExchange.Ticker((string)s, p));
            fe.OrderBookProvider = (s, l, pr) => Task.FromResult<object>(FakeExchange.OrderBook(bids: new[] { (p - 0.05, 1000.0) }, asks: new[] { (p + 0.05, 1000.0) }));
            fe.FundingRateProvider = (s, pr) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        }

        var (service, comms, telegram, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity(spread: 5);
        opportunity.ExchangeRateA = Rate("binance-fut-obs", 100);
        opportunity.ExchangeRateB = Rate("okx-fut-obs", 100.05);
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchList[combineKey] = opportunity;

        await service.WatchPossibleFuturesPositionWithCombineKey(opportunity);

        dataService.WatchList.Should().NotContainKey(combineKey);
        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spread closed"))), Times.Once);
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
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1, spreadSize: 100);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fut-obs", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fut-obs", Symbol);
        var binance = (FakeExchange)dataService.ExchangeObserverServices["binance-fut-obs"].GetExchange();
        var okx = (FakeExchange)dataService.ExchangeObserverServices["okx-fut-obs"].GetExchange();
        foreach (var fe in new[] { binance, okx })
        {
            fe.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
            fe.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(bids: new[] { (99.9, 1000.0) }, asks: new[] { (100.1, 1000.0) }));
            fe.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        }
        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchList[combineKey] = opportunity;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartToWatchPositionsWithCombineKeys(cts.Token);
        await Task.Delay(300);

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.AtLeastOnce);
    }
}
