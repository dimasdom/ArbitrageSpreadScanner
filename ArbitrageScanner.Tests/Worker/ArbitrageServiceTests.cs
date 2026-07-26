using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using ArbitrageScanner.Worker;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Worker;

public class ArbitrageServiceTests
{
    [Fact]
    public void Constructor_EmptyExchangeList_DoesNotThrow()
    {
        var act = () => ServiceFactory.BuildArbitrageService();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ProcessSymbol_NoExchangeServices_CompletesWithoutThrowing()
    {
        var service = ServiceFactory.BuildArbitrageService();

        var act = async () => await service.ProcessSymbol("BTC/USDT:USDT", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessSymbol_WithExchangeServiceData_CompletesWithoutThrowing()
    {
        var dataService = ServiceFactory.BuildDataService();
        var fake = new FakeExchange("binance");
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 1000.0) },
            asks: new[] { (100.1, 1000.0) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false) });

        var config = ServiceFactory.BuildConfig(positionSize: 1);
        var exchangeService = new ExchangeService(dataService, config);
        await exchangeService.Init(fake);
        await exchangeService.LoadSwapMarkets();
        dataService.ExchangeServices["binance"] = exchangeService;

        var service = ServiceFactory.BuildArbitrageService(config, dataService);

        var act = async () => await service.ProcessSymbol("BTC/USDT:USDT", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessSymbol_ImmediatelyCancelled_ReturnsWithoutThrowing()
    {
        var service = ServiceFactory.BuildArbitrageService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.ProcessSymbol("BTC/USDT:USDT", cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperations_PreCancelledToken_ReturnsImmediately()
    {
        var service = ServiceFactory.BuildArbitrageService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.StartOperations(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperationParallel_PreCancelledToken_ReturnsImmediately()
    {
        var service = ServiceFactory.BuildArbitrageService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.StartOperationParallel(0, cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperation_WithOneExchangeService_LoadsMarketsAndReturnsOnCancellation()
    {
        Environment.SetEnvironmentVariable("NODE_TOTAL", null);
        Environment.SetEnvironmentVariable("NODE_INDEX", null);

        var dataService = ServiceFactory.BuildDataService();
        var fake = new FakeExchange("kraken");
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false) });
        var config = ServiceFactory.BuildConfig();
        var exchangeService = new ExchangeService(dataService, config);
        await exchangeService.Init(fake);
        dataService.ExchangeServices["kraken"] = exchangeService;
        dataService.ExchangeObserverServices["kraken"] = exchangeService;

        var service = ServiceFactory.BuildArbitrageService(config, dataService);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.StartOperation(parallel: false, cts.Token);

        await act.Should().NotThrowAsync();
        exchangeService.markets.Should().ContainKey("BTC/USDT:USDT");
    }
}
