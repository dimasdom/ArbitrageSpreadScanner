using System.Globalization;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Spot.Services;
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

    internal static DataService BuildDataService(IConfiguration? config = null)
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        mockRepo.Setup(r => r.SaveError(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));
        return new DataService(mockRepo.Object, config ?? BuildConfig());
    }

    internal static FuturesPositionCalculatorService BuildFuturesCalculator(IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        return new FuturesPositionCalculatorService(cfg, BuildDataService(cfg));
    }

    internal static FundingPositionCalculatorService BuildFundingCalculator(IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        return new FundingPositionCalculatorService(cfg, BuildDataService(cfg));
    }

    internal static SpotPositionCalculatorService BuildSpotCalculator(IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        return new SpotPositionCalculatorService(cfg, BuildDataService(cfg));
    }

    internal static FundingObserverService BuildFundingObserver(IConfiguration? config = null)
    {
        var cfg = config ?? BuildConfig();
        var dataService = BuildDataService(cfg);
        var calculator = new FundingPositionCalculatorService(cfg, dataService);
        var mockComms = new Mock<IServicesCommunicationService>();
        var mockTelegram = new Mock<ITelegramNotifierService>();
        var ui = new UserInterfaceService(cfg, dataService, mockTelegram.Object);
        return new FundingObserverService(dataService, cfg, calculator, mockComms.Object, ui);
    }
}
