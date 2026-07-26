using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class ExchangeServiceTests
{
    private static IConfiguration BuildConfig(bool spot = false, double positionSize = 10_000)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arbitrage:PositionSize"] = positionSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Arbitrage:Spot"] = spot.ToString(),
            })
            .Build();
    }

    private static ExchangeService BuildService(FakeExchange fake, bool spot = false, double positionSize = 10_000)
    {
        var dataService = ServiceFactory.BuildDataService(BuildConfig(spot, positionSize));
        var service = new ExchangeService(dataService, BuildConfig(spot, positionSize));
        service.Init(fake).Wait();
        return service;
    }

    [Fact]
    public void GetExchangeName_ReturnsExchangeName()
    {
        var fake = new FakeExchange("binance");
        var service = BuildService(fake);

        service.GetExchangeName().Should().Be("binance");
        service.GetExchange().Should().BeSameAs(fake);
    }

    [Fact]
    public async Task GetActualRate_ReturnsLastPrice()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 123.45));
        var service = BuildService(fake);

        var rate = await service.GetActualRate("BTC/USDT");

        rate.Should().Be(123.45);
    }

    [Fact]
    public async Task GetActualRate_NoLastValue_ReturnsZero()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, null));
        var service = BuildService(fake);

        var rate = await service.GetActualRate("BTC/USDT");

        rate.Should().Be(0);
    }

    [Fact]
    public async Task GetActualRate_ThrowingExchange_ReturnsZeroAndLogsError()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => throw new InvalidOperationException("boom");
        var service = BuildService(fake);

        var rate = await service.GetActualRate("BTC/USDT");

        rate.Should().Be(0);
    }

    [Fact]
    public async Task GetFundingInfo_ReturnsFundingRate()
    {
        var fake = new FakeExchange();
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0002));
        var service = BuildService(fake);

        var result = await service.GetFundingInfo("BTC/USDT:USDT");

        result.Should().NotBeNull();
        result!.Value.fundingRate.Should().Be(0.0002);
    }

    [Fact]
    public async Task GetFundingInfo_Throws_ReturnsNull()
    {
        var fake = new FakeExchange();
        fake.FundingRateProvider = (s, p) => throw new InvalidOperationException("boom");
        var service = BuildService(fake);

        var result = await service.GetFundingInfo("BTC/USDT:USDT");

        result.Should().BeNull();
    }

    private static async Task<ExchangeService> BuildWithSwapMarketLoaded(FakeExchange fake, string symbol = "BTC/USDT:USDT", bool spot = false, double positionSize = 10_000)
    {
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market(symbol, swap: true, spot: false) });
        var service = BuildService(fake, spot, positionSize);
        await service.LoadSwapMarkets();
        return service;
    }

    [Fact]
    public async Task GetDataForCoin_UnknownSymbol_ReturnsNull()
    {
        var fake = new FakeExchange();
        var service = await BuildWithSwapMarketLoaded(fake);

        var result = await service.GetDataForCoin("ETH/USDT:USDT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDataForCoin_FuturesSymbol_BuildsRateWithoutSpot()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 1000.0) },
            asks: new[] { (100.1, 1000.0) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 1);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", onlyFutures: true);

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("BTC/USDT:USDT");
        result.ExchangeRate.Should().Be(100);
        result.FundingRateValue.Should().Be(0.0001);
        result.SpotTicker.Should().BeNull();
    }

    [Fact]
    public async Task GetDataForCoin_SpotSymbol_ReusesMainDataAsSpotTicker()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 1000.0) },
            asks: new[] { (100.1, 1000.0) }));
        var dataService = ServiceFactory.BuildDataService(BuildConfig(positionSize: 1));
        var service = new ExchangeService(dataService, BuildConfig(positionSize: 1));
        await service.Init(fake);
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT", swap: false, spot: true) });
        await service.LoadSpotMarkets();

        var result = await service.GetDataForCoin("BTC/USDT");

        result.Should().NotBeNull();
        result!.SpotTicker.Should().NotBeNull();
        result.SpotTicker!.Symbol.Should().Be("BTC/USDT");
    }

    [Fact]
    public async Task GetDataForCoin_FuturesSymbol_FetchesSpotWhenConfigured()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 1000.0) },
            asks: new[] { (100.1, 1000.0) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));

        var dataService = ServiceFactory.BuildDataService(BuildConfig(spot: true, positionSize: 1));
        var service = new ExchangeService(dataService, BuildConfig(spot: true, positionSize: 1));
        await service.Init(fake);

        // ccxt's LoadMarkets() caches per exchange instance after the first successful call, so
        // both swap and spot markets must be present in the single response the first Load*Markets call sees.
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("BTC/USDT", swap: false, spot: true),
        });
        await service.LoadSwapMarkets();
        await service.LoadSpotMarkets();

        var result = await service.GetDataForCoin("BTC/USDT:USDT");

        result.Should().NotBeNull();
        result!.SpotTicker.Should().NotBeNull();
        result.SpotTicker!.Symbol.Should().Be("BTC/USDT");
    }

    [Fact]
    public async Task GetDataForCoin_LowLiquidity_ReturnsNullWhenCheckEnabled()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 0.01) },
            asks: new[] { (100.1, 0.01) }));
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 10_000);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", checkLiquidity: true, onlyFutures: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDataForCoin_LowLiquidity_ReturnsDataWhenCheckDisabled()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, 100));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 0.01) },
            asks: new[] { (100.1, 0.01) }));
        fake.FundingRateProvider = (s, p) => Task.FromResult<object>(FakeExchange.FundingRate((string)s, 0.0001));
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 10_000);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", checkLiquidity: false, onlyFutures: true);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDataForCoin_MissingTickerLast_ReturnsNull()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => Task.FromResult<object>(FakeExchange.Ticker((string)s, null));
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook(
            bids: new[] { (99.9, 1000.0) },
            asks: new[] { (100.1, 1000.0) }));
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 1);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", onlyFutures: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDataForCoin_MainFetchTimesOut_ReturnsNull()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = async (s, p) => { await Task.Delay(2000); return FakeExchange.Ticker((string)s, 100); };
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook());
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 1);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", timeoutSeconds: 0, onlyFutures: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDataForCoin_ExchangeThrows_ReturnsNull()
    {
        var fake = new FakeExchange();
        fake.TickerProvider = (s, p) => throw new InvalidOperationException("boom");
        fake.OrderBookProvider = (s, l, p) => Task.FromResult<object>(FakeExchange.OrderBook());
        var service = await BuildWithSwapMarketLoaded(fake, positionSize: 1);

        var result = await service.GetDataForCoin("BTC/USDT:USDT", onlyFutures: true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadSwapPairs_ReturnsOnlySwapSymbols()
    {
        var fake = new FakeExchange();
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
        });
        var service = BuildService(fake);

        var pairs = await service.LoadSwapPairs();

        pairs.Should().ContainSingle().Which.Should().Be("BTC/USDT:USDT");
    }

    [Fact]
    public async Task LoadSwapPairs_Throws_ReturnsEmpty()
    {
        var fake = new FakeExchange();
        fake.MarketsProvider = _ => throw new InvalidOperationException("boom");
        var service = BuildService(fake);

        var pairs = await service.LoadSwapPairs();

        pairs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadSpotPairs_ReturnsOnlyUsdtSpotSymbols()
    {
        var fake = new FakeExchange();
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
        });
        var service = BuildService(fake);

        var pairs = await service.LoadSpotPairs();

        pairs.Should().ContainSingle().Which.Should().Be("ETH/USDT");
    }

    [Fact]
    public async Task LoadSwapMarkets_FirstLoad_PopulatesMarketsAndDataServiceCache()
    {
        var fake = new FakeExchange("kraken");
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
        });
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service = new ExchangeService(dataService, BuildConfig());
        await service.Init(fake);

        await service.LoadSwapMarkets();

        service.markets.Should().ContainKey("BTC/USDT:USDT");
        service.markets.Should().NotContainKey("ETH/USDT");
        dataService.ExchangeMarkets.Should().ContainKey("kraken");
        dataService.ExchangeMarkets["kraken"].Should().ContainKey("BTC/USDT:USDT");
    }

    [Fact]
    public async Task LoadSwapMarkets_AlreadyCached_CopiesFromDataServiceWithoutRefetch()
    {
        var fake = new FakeExchange("kraken");
        var callCount = 0;
        fake.MarketsProvider = _ =>
        {
            callCount++;
            return Task.FromResult<object>(new List<object> { FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false) });
        };
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service1 = new ExchangeService(dataService, BuildConfig());
        await service1.Init(fake);
        await service1.LoadSwapMarkets();
        callCount.Should().Be(1);

        var service2 = new ExchangeService(dataService, BuildConfig());
        await service2.Init(fake);
        await service2.LoadSwapMarkets();

        callCount.Should().Be(1);
        service2.markets.Should().ContainKey("BTC/USDT:USDT");
    }

    [Fact]
    public async Task LoadSwapMarkets_Throws_DoesNotPropagate()
    {
        var fake = new FakeExchange();
        fake.MarketsProvider = _ => throw new InvalidOperationException("boom");
        var service = BuildService(fake);

        var act = async () => await service.LoadSwapMarkets();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoadSpotMarkets_FirstLoad_PopulatesSpotMarketsAndDataServiceCache()
    {
        var fake = new FakeExchange("okx");
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
        });
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service = new ExchangeService(dataService, BuildConfig());
        await service.Init(fake);

        await service.LoadSpotMarkets();

        service.spotMarkets.Should().ContainKey("ETH/USDT");
        service.spotMarkets.Should().NotContainKey("BTC/USDT:USDT");
        dataService.ExchangeSpotMarkets["okx"].Should().ContainKey("ETH/USDT");
    }

    [Fact]
    public async Task LoadSpotMarkets_AlreadyCached_CopiesFromDataService()
    {
        var fake = new FakeExchange("okx");
        var callCount = 0;
        fake.MarketsProvider = _ =>
        {
            callCount++;
            return Task.FromResult<object>(new List<object> { FakeExchange.Market("ETH/USDT", swap: false, spot: true) });
        };
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service1 = new ExchangeService(dataService, BuildConfig());
        await service1.Init(fake);
        await service1.LoadSpotMarkets();

        var service2 = new ExchangeService(dataService, BuildConfig());
        await service2.Init(fake);
        await service2.LoadSpotMarkets();

        callCount.Should().Be(1);
        service2.spotMarkets.Should().ContainKey("ETH/USDT");
    }

    [Fact]
    public async Task UpdatePairs_AddsNewlyDiscoveredSwapAndSpotPairs()
    {
        var dataService = ServiceFactory.BuildDataService(BuildConfig());

        // Seed the "already known" caches via a throwaway exchange instance/service.
        var seedFake = new FakeExchange("bybit");
        var seedService = new ExchangeService(dataService, BuildConfig());
        await seedService.Init(seedFake);
        seedFake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
        });
        await seedService.LoadSwapMarkets();
        await seedService.LoadSpotMarkets();

        // A fresh exchange instance so its own LoadMarkets() cache is empty and UpdatePairs sees the full superset.
        var fake = new FakeExchange("bybit");
        var service = new ExchangeService(dataService, BuildConfig());
        await service.Init(fake);
        fake.MarketsProvider = _ => Task.FromResult<object>(new List<object>
        {
            FakeExchange.Market("BTC/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("SOL/USDT:USDT", swap: true, spot: false),
            FakeExchange.Market("ETH/USDT", swap: false, spot: true),
            FakeExchange.Market("ADA/USDT", swap: false, spot: true),
        });

        await service.UpdatePairs();

        service.markets.Should().ContainKey("SOL/USDT:USDT");
        service.spotMarkets.Should().ContainKey("ADA/USDT");
        dataService.ExchangeMarkets["bybit"].Should().ContainKey("SOL/USDT:USDT");
        dataService.ExchangeSpotMarkets["bybit"].Should().ContainKey("ADA/USDT");
    }

    [Fact]
    public async Task UpdatePairs_Throws_DoesNotPropagate()
    {
        var fake = new FakeExchange("bybit");
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service = new ExchangeService(dataService, BuildConfig());
        await service.Init(fake);
        fake.MarketsProvider = _ => throw new InvalidOperationException("boom");

        var act = async () => await service.UpdatePairs();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Init_SetsExchangeInstance()
    {
        var fake = new FakeExchange("gate");
        var dataService = ServiceFactory.BuildDataService(BuildConfig());
        var service = new ExchangeService(dataService, BuildConfig());

        await service.Init(fake);

        service.GetExchange().Should().BeSameAs(fake);
        service.GetExchangeName().Should().Be("gate");
    }
}
