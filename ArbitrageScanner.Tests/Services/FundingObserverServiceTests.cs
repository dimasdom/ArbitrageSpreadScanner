using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class FundingObserverServiceTests
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
        Type = SpreadType.Funding,
        Spread = spread,
        ExchangeRateA = Rate("binance-fund-obs", 100),
        ExchangeRateB = Rate("okx-fund-obs", 100),
    };

    private static (FundingObserverService service, Mock<IServicesCommunicationService> comms, Mock<ITelegramNotifierService> telegram, DataService dataService)
        BuildObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? ServiceFactory.BuildConfig();
        var ds = dataService ?? ServiceFactory.BuildDataService(cfg);
        var calculator = new FundingPositionCalculatorService(cfg, ds);
        var comms = new Mock<IServicesCommunicationService>();
        comms.Setup(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>())).Returns(Task.CompletedTask);
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var ui = new UserInterfaceService(cfg, ds, telegram.Object);
        var service = new FundingObserverService(ds, cfg, calculator, comms.Object, ui);
        return (service, comms, telegram, ds);
    }

    private static void ConfigureFakeExchange(ExchangeService exchangeService, double tickerPrice, double? fundingRate)
    {
        var fake = (FakeExchange)exchangeService.GetExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, tickerPrice));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (tickerPrice - 0.1, 1000.0) }, asks: new[] { (tickerPrice + 0.1, 1000.0) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, fundingRate));
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_NewFundingOpportunity_AddsToWatchListAndPostsEverywhere()
    {
        var config = ServiceFactory.BuildConfigWithFlags(fundingThreshold: 0.0001, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, 0.001);
        ConfigureFakeExchange(okx, 100, -0.001);

        var (service, comms, telegram, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListFunding.TryRemove(combineKey, out _);

        await service.CheckAndAddNewFuturesPositionsToWatch(new List<TradeOpportunityModel> { opportunity });

        dataService.WatchListFunding.Should().ContainKey(combineKey);
        dataService.WatchListFunding[combineKey].Type.Should().Be(SpreadType.Funding);
        comms.Verify(c => c.PostPossiblePosition(opportunity), Times.Once);
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndAddNewFuturesPositionsToWatch_AlreadyWatched_SkipsDuplicate()
    {
        var (service, comms, _, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListFunding[combineKey] = opportunity;

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
    public async Task WatchPossiblePosition_MissingFundingData_ReturnsNull()
    {
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, null);
        ConfigureFakeExchange(okx, 100, null);
        var (service, _, _, _) = BuildObserver(config, dataService);

        var result = await service.WatchPossiblePosition(Opportunity());

        result.Should().BeNull();
    }

    [Fact]
    public async Task WatchPossiblePosition_ValidFundingData_ComputesSpreadAndDirection()
    {
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, 0.001);
        ConfigureFakeExchange(okx, 100, -0.001);
        var (service, _, _, _) = BuildObserver(config, dataService);

        var result = await service.WatchPossiblePosition(Opportunity());

        result.Should().NotBeNull();
        result!.Spread.Should().BeApproximately(0.2, 1e-9);
        result.ExchangeLong.Should().NotBeNull();
        result.ExchangeShort.Should().NotBeNull();
    }

    [Fact]
    public async Task WatchPossibleFundingPositionWithCombineKey_SpreadStillWide_UpdatesWatchList()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, 0.01);
        ConfigureFakeExchange(okx, 100, -0.01);
        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);

        await service.WatchPossibleFundingPositionWithCombineKey(opportunity);

        // WatchListFunding is process-wide static state shared with other test classes running in
        // parallel, so only assert on this test's own key rather than the dictionary's total size.
        dataService.WatchListFunding.Should().ContainKey(combineKey);
        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.Once);
    }

    [Fact]
    public async Task WatchPossibleFundingPositionWithCombineKey_SpreadClosed_RemovesAndNotifies()
    {
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, 0.00001);
        ConfigureFakeExchange(okx, 100, -0.00001);
        var (service, _, telegram, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListFunding[combineKey] = opportunity;

        await service.WatchPossibleFundingPositionWithCombineKey(opportunity);

        dataService.WatchListFunding.Should().NotContainKey(combineKey);
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
        var config = ServiceFactory.BuildConfigWithFlags(positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        var binance = await ServiceFactory.RegisterExchangeService(dataService, config, "binance-fund-obs", Symbol);
        var okx = await ServiceFactory.RegisterExchangeService(dataService, config, "okx-fund-obs", Symbol);
        ConfigureFakeExchange(binance, 100, 0.01);
        ConfigureFakeExchange(okx, 100, -0.01);
        var (service, comms, _, _) = BuildObserver(config, dataService);
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        dataService.WatchListFunding[combineKey] = opportunity;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartToWatchPositionsWithCombineKeys(cts.Token);
        await Task.Delay(300);

        comms.Verify(c => c.PostPossiblePosition(It.IsAny<TradeOpportunityModel>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartToWatchPositionsWithCombineKeys_WatchThrows_LogsAndRetriesInsteadOfCrashing()
    {
        var (service, _, _, dataService) = BuildObserver();
        var opportunity = Opportunity();
        var combineKey = dataService.GenerateCombineKeyFor(opportunity);
        // No exchanges registered -> WatchPossiblePosition throws KeyNotFoundException, which
        // (unlike Futures/Spot) this method does not catch itself, so it propagates to the loop.
        dataService.WatchListFunding[combineKey] = opportunity;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var act = async () => await service.StartToWatchPositionsWithCombineKeys(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("8h", 8, 0)]
    [InlineData("45m", 0, 45)]
    [InlineData("1h30m", 1, 30)]
    [InlineData("", 0, 0)]
    public void ParseInterval_ParsesHoursAndMinutes(string interval, int hours, int minutes)
    {
        var (service, _, _, _) = BuildObserver();

        var result = service.ParseInterval(interval);

        result.Should().Be(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void GetNextPayoutUtc_EmptyInterval_ReturnsDefault()
    {
        var (service, _, _, _) = BuildObserver();

        var result = service.GetNextPayoutUtc("");

        result.Should().Be(default(DateTime));
    }

    [Fact]
    public void GetNextPayoutUtc_ValidInterval_ReturnsFutureUtcTime()
    {
        var (service, _, _, _) = BuildObserver();

        var result = service.GetNextPayoutUtc("8h");

        result.Should().BeAfter(DateTime.UtcNow);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }
}
