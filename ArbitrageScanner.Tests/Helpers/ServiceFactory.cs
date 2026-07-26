using System.Globalization;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Spot.Services;
using ArbitrageScanner.Worker;
using ArbitrageScanner.Worker.Controllers;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ArbitrageScanner.Tests.Helpers;

internal static class ServiceFactory
{
    internal static IConfiguration BuildConfig(double spreadSize = 0.5, double fundingThreshold = 0.01, double positionSize = 10_000)
    {
        var ic = CultureInfo.InvariantCulture;
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arbitrage:SpreadSize"] = spreadSize.ToString(ic),
                ["Arbitrage:FundingThresholdRatio"] = fundingThreshold.ToString(ic),
                ["Arbitrage:PositionSize"] = positionSize.ToString(ic),
                ["Arbitrage:KeepWatchingSpread"] = "0.1",
                ["Arbitrage:ThreadCount"] = "1"
            })
            .Build();
    }

    internal static IConfiguration BuildConfigWithFlags(bool futures = false, bool funding = false, bool spot = false, double spreadSize = 0.5, double fundingThreshold = 0.01, double positionSize = 1)
    {
        var ic = CultureInfo.InvariantCulture;
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arbitrage:SpreadSize"] = spreadSize.ToString(ic),
                ["Arbitrage:FundingThresholdRatio"] = fundingThreshold.ToString(ic),
                ["Arbitrage:PositionSize"] = positionSize.ToString(ic),
                ["Arbitrage:KeepWatchingSpread"] = "0.1",
                ["Arbitrage:ThreadCount"] = "1",
                ["Arbitrage:Futures"] = futures.ToString(),
                ["Arbitrage:Funding"] = funding.ToString(),
                ["Arbitrage:Spot"] = spot.ToString(),
            })
            .Build();
    }

    internal static DataService BuildDataService(IConfiguration? config = null)
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        mockRepo.Setup(r => r.SaveError(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));
        return new DataService(mockRepo.Object, config ?? BuildConfig());
    }

    internal static FuturesPositionCalculatorService BuildFuturesCalculator(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        return new FuturesPositionCalculatorService(cfg, dataService ?? BuildDataService(cfg));
    }

    internal static FundingPositionCalculatorService BuildFundingCalculator(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        return new FundingPositionCalculatorService(cfg, dataService ?? BuildDataService(cfg));
    }

    internal static SpotPositionCalculatorService BuildSpotCalculator(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        return new SpotPositionCalculatorService(cfg, dataService ?? BuildDataService(cfg));
    }

    internal static FundingObserverService BuildFundingObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        var ds = dataService ?? BuildDataService(cfg);
        var calculator = new FundingPositionCalculatorService(cfg, ds);
        var mockComms = new Mock<IServicesCommunicationService>();
        var mockTelegram = new Mock<ITelegramNotifierService>();
        var ui = new UserInterfaceService(cfg, ds, mockTelegram.Object);
        return new FundingObserverService(ds, cfg, calculator, mockComms.Object, ui);
    }

    internal static FuturesObserverService BuildFuturesObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        var ds = dataService ?? BuildDataService(cfg);
        var calculator = new FuturesPositionCalculatorService(cfg, ds);
        var mockComms = new Mock<IServicesCommunicationService>();
        var mockTelegram = new Mock<ITelegramNotifierService>();
        var ui = new UserInterfaceService(cfg, ds, mockTelegram.Object);
        return new FuturesObserverService(ds, cfg, calculator, mockComms.Object, ui);
    }

    internal static SpotObserverService BuildSpotObserver(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        var ds = dataService ?? BuildDataService(cfg);
        var calculator = new SpotPositionCalculatorService(cfg, ds);
        var mockComms = new Mock<IServicesCommunicationService>();
        var mockTelegram = new Mock<ITelegramNotifierService>();
        var ui = new UserInterfaceService(cfg, ds, mockTelegram.Object);
        return new SpotObserverService(ds, cfg, calculator, mockComms.Object, ui);
    }

    internal static ArbitrageStrategyOrchestrator BuildOrchestrator(IConfiguration? config = null, DataService? dataService = null)
    {
        var cfg = config ?? BuildConfig();
        var ds = dataService ?? BuildDataService(cfg);
        return new ArbitrageStrategyOrchestrator(
            cfg,
            BuildFuturesCalculator(cfg, ds),
            BuildFundingCalculator(cfg, ds),
            BuildSpotCalculator(cfg, ds),
            BuildFuturesObserver(cfg, ds),
            BuildFundingObserver(cfg, ds),
            BuildSpotObserver(cfg, ds));
    }

    /// <summary>
    /// Creates a FakeExchange-backed ExchangeService with the given swap (and optionally spot) market
    /// loaded, and registers it in both ExchangeServices and ExchangeObserverServices — the two lookups
    /// the Futures/Funding/Spot calculators use to resolve maker fees.
    /// </summary>
    internal static async Task<ExchangeService> RegisterExchangeService(
        DataService dataService, IConfiguration config, string name, string swapSymbol, string? spotSymbol = null)
    {
        var fake = new FakeExchange(name);
        var markets = new List<object> { FakeExchange.Market(swapSymbol, swap: true, spot: false) };
        if (spotSymbol is not null)
            markets.Add(FakeExchange.Market(spotSymbol, swap: false, spot: true));
        fake.MarketsProvider = _ => Task.FromResult<object>(markets);

        var service = new ExchangeService(dataService, config);
        await service.Init(fake);
        await service.LoadSwapMarkets();
        if (spotSymbol is not null)
            await service.LoadSpotMarkets();

        dataService.ExchangeServices[name] = service;
        dataService.ExchangeObserverServices[name] = service;
        return service;
    }

    internal static ArbitrageService BuildArbitrageService(IConfiguration? config = null, DataService? dataService = null, IProxyService? proxyService = null)
    {
        var cfg = config ?? BuildConfig();
        var ds = dataService ?? BuildDataService(cfg);
        var proxy = proxyService ?? Mock.Of<IProxyService>();
        return new ArbitrageService(
            BuildOrchestrator(cfg, ds),
            BuildFuturesObserver(cfg, ds),
            BuildFundingObserver(cfg, ds),
            BuildSpotObserver(cfg, ds),
            proxy,
            ds,
            cfg);
    }
}
