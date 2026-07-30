using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

[Collection(SharedProxyPoolCollection.Name)]
public class DataServiceTests
{
    private static ExchangeRateModel Rate(string symbol, string exchange, double rate = 100) => new()
    {
        Symbol = symbol,
        Exchange = exchange,
        ExchangeRate = rate,
    };

    // Exchange names deliberately distinct from other test files (e.g. "binance"/"okx") — DataService's
    // WatchList* dictionaries are static/process-wide, and identical combine keys across parallel test
    // classes caused cross-test flakiness.
    private static TradeOpportunityModel Opportunity(SpreadType type = SpreadType.Futures) => new()
    {
        Guid = Guid.NewGuid(),
        Type = type,
        Symbol = "BTC/USDT:USDT",
        ExchangeRateA = Rate("BTC/USDT:USDT", "binance-ds-test"),
        ExchangeRateB = Rate("BTC/USDT:USDT", "okx-ds-test"),
        ExchangeLong = Rate("BTC/USDT:USDT", "binance-ds-test"),
        ExchangeShort = Rate("BTC/USDT:USDT", "okx-ds-test"),
    };

    [Fact]
    public void LogErrorEntry_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var ex = new InvalidOperationException("boom");

        service.LogErrorEntry(ex, "BTC/USDT", "Method", "binance");

        mockRepo.Verify(r => r.SaveError(ex, "BTC/USDT", "Method", "binance"), Times.Once);
    }

    [Fact]
    public async Task LoadProxiesAsync_ReplacesProxyPool()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var proxies = new List<ProxyModel> { new() { ip = "1.2.3.4", port = 8080 } };
        mockRepo.Setup(r => r.LoadProxies()).ReturnsAsync(proxies);
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);

        await service.LoadProxiesAsync();

        DataService.ProxiesList.Should().Contain(p => p.ip == "1.2.3.4");
    }

    [Theory]
    [InlineData(SpreadType.Futures)]
    [InlineData(SpreadType.Funding)]
    [InlineData(SpreadType.Spot)]
    public async Task LoadActivePossiblePositionsAsync_RoutesToCorrectWatchListByType(SpreadType type)
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var opportunity = Opportunity(type);
        var ticker = new TradeOpportunityTickerModel(opportunity);
        mockRepo.Setup(r => r.GetActivePossiblePositions())
            .ReturnsAsync(new List<TradeOpportunityTickerModel> { ticker });
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var combineKey = DataService.GenerateCombineKey(new TradeOpportunityModel
        {
            ExchangeRateA = new ExchangeRateModel { Symbol = ticker.Symbol, Exchange = ticker.ExchangeA },
            ExchangeRateB = new ExchangeRateModel { Symbol = ticker.Symbol, Exchange = ticker.ExchangeB },
        });

        await service.LoadActivePossiblePositionsAsync();

        var targetList = type switch
        {
            SpreadType.Futures => DataService.watchList,
            SpreadType.Funding => DataService.watchListFunding,
            SpreadType.Spot => DataService.watchListSpot,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        targetList.Should().ContainKey(combineKey);
        targetList[combineKey].Guid.Should().Be(opportunity.Guid);
    }

    [Fact]
    public void GenerateCombineKeyFor_CombinesSymbolAndBothExchanges()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        var key = service.GenerateCombineKeyFor(opportunity);

        key.Should().Be("BTC/USDT:USDTbinance-ds-test okx-ds-test");
    }

    [Fact]
    public async Task AddActivePossiblePositionAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.AddActivePossiblePositionAsync(opportunity);

        mockRepo.Verify(r => r.AddActivePossiblePosition(opportunity), Times.Once);
    }

    [Fact]
    public async Task DeleteActivePossiblePositionAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.DeleteActivePossiblePositionAsync(opportunity);

        mockRepo.Verify(r => r.DeleteActivePossiblePosition(opportunity), Times.Once);
    }

    [Fact]
    public async Task UpdateActivePossiblePositionAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.UpdateActivePossiblePositionAsync(opportunity);

        mockRepo.Verify(r => r.UpdateActivePossiblePosition(opportunity), Times.Once);
    }

    [Fact]
    public async Task SaveSpreadsTickerAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.SaveSpreadsTickerAsync(opportunity);

        mockRepo.Verify(r => r.SaveSpreadsTicker(opportunity), Times.Once);
    }

    [Fact]
    public async Task SaveFoundSpreadAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.SaveFoundSpreadAsync(opportunity);

        mockRepo.Verify(r => r.SaveFoundSpread(opportunity), Times.Once);
    }

    [Fact]
    public async Task SaveFoundFundingSpreadAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.SaveFoundFundingSpreadAsync(opportunity);

        mockRepo.Verify(r => r.SaveFoundFundingSpread(opportunity), Times.Once);
    }

    [Fact]
    public async Task SaveFoundSpotSpreadAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.SaveFoundSpotSpreadAsync(opportunity);

        mockRepo.Verify(r => r.SaveFoundSpotSpread(opportunity), Times.Once);
    }

    [Fact]
    public async Task SaveSpotSpreadsTickerToMongoAsync_DelegatesToRepository()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var service = new DataService(mockRepo.Object, ServiceFactory.BuildConfig(), NullLogger<DataService>.Instance);
        var opportunity = Opportunity();

        await service.SaveSpotSpreadsTickerToMongoAsync(opportunity);

        mockRepo.Verify(r => r.SaveSpotSpreadsTicker(opportunity), Times.Once);
    }

    [Fact]
    public async Task GetUniqueCommonFuturesPairsFromApiAsync_ReturnsPairsCommonToAllExchangeServices()
    {
        Environment.SetEnvironmentVariable("NODE_TOTAL", null);
        Environment.SetEnvironmentVariable("NODE_INDEX", null);
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        var config = ServiceFactory.BuildConfig();
        var service = new DataService(mockRepo.Object, config, NullLogger<DataService>.Instance);

        var sharedPair = "BTC/USDT:USDT";
        for (var i = 0; i < 5; i++)
        {
            var fake = new FakeExchange($"exchange{i}");
            fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
            {
                FakeExchange.Market(sharedPair, swap: true, spot: false),
            });
            var exchangeService = new ExchangeService(service, config);
            await exchangeService.Init(fake);
            service.ExchangeServices[$"exchange{i}"] = exchangeService;
        }

        var result = await service.GetUniqueCommonFuturesPairsFromApiAsync();

        result.Should().Contain(sharedPair);
    }
}
