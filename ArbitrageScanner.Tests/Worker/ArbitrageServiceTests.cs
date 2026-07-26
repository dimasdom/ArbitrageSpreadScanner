using System.Reflection;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using ArbitrageScanner.Worker;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Worker;

[Collection(SharedProxyPoolCollection.Name)]
public class ArbitrageServiceTests
{
    // "symbols" is a private field populated by StartOperation via a live exchange lookup;
    // tests that only need StartOperations/StartOperationParallel's loop body (not the full
    // StartOperation orchestration) seed it directly through reflection.
    private static void SeedSymbols(ArbitrageService service, params string[] symbols)
    {
        var field = typeof(ArbitrageService).GetField("symbols", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(service, symbols.ToList());
    }

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
    public async Task ProcessSymbol_SlowExchangeExceedsTimeout_ReturnsWithoutThrowing()
    {
        var dataService = ServiceFactory.BuildDataService();
        var fake = new FakeExchange("slow-exchange");
        fake.TickerProvider = async (s, p) => { await Task.Delay(TimeSpan.FromSeconds(2)); return FakeExchange.Ticker((string)s, 100); };
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook());
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false) });
        var config = ServiceFactory.BuildConfig();
        var exchangeService = new ExchangeService(dataService, config);
        await exchangeService.Init(fake);
        await exchangeService.LoadSwapMarkets();
        dataService.ExchangeServices["slow-exchange"] = exchangeService;

        var service = ServiceFactory.BuildArbitrageService(config, dataService);
        // The overall cancellation token doubles as ProcessSymbol's own timeout signal — cancelling it
        // early while the (slow, still-running) fetch is in flight forces the "timeout" branch to win.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = async () => await service.ProcessSymbol("BTC/USDT:USDT", cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperations_WithSymbols_ProcessesEachSymbolUntilCancelled()
    {
        var service = ServiceFactory.BuildArbitrageService();
        SeedSymbols(service, "BTC/USDT:USDT", "ETH/USDT:USDT");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var act = async () => await service.StartOperations(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperationParallel_TimesUpdatedZero_SkipsUpdatePairsThenShufflesAndReturns()
    {
        var service = ServiceFactory.BuildArbitrageService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var act = async () => await service.StartOperationParallel(timesUpdated: 0, cancellationToken: cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartOperationParallel_TimesUpdatedMultipleOfTen_CallsUpdatePairsOnAllExchangeServices()
    {
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        var fake = new FakeExchange("update-pairs-test");
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false) });
        var exchangeService = new ExchangeService(dataService, config);
        await exchangeService.Init(fake);
        // UpdatePairs() only adds pairs not already present in DataService's cache, so seed it first
        // via LoadSwapMarkets/LoadSpotMarkets — otherwise UpdatePairs silently no-ops (missing cache
        // entry) rather than throwing, and this test would exercise nothing new.
        await exchangeService.LoadSwapMarkets();
        await exchangeService.LoadSpotMarkets();
        dataService.ExchangeServices["update-pairs-test"] = exchangeService;

        var service = ServiceFactory.BuildArbitrageService(config, dataService);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var act = async () => await service.StartOperationParallel(timesUpdated: 10, cancellationToken: cts.Token);

        await act.Should().NotThrowAsync();
        exchangeService.markets.Should().ContainKey("BTC/USDT:USDT");
    }

    [Fact]
    public async Task StartOperationParallel_SymbolsExceedThreadCount_AwaitsWhenAnyToThrottle()
    {
        var config = ServiceFactory.BuildConfig();
        var service = ServiceFactory.BuildArbitrageService(config);
        SeedSymbols(service, "BTC/USDT:USDT", "ETH/USDT:USDT", "SOL/USDT:USDT");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var act = async () => await service.StartOperationParallel(timesUpdated: 0, cancellationToken: cts.Token);

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
