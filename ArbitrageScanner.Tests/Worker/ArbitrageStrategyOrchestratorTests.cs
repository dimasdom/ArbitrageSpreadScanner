using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Worker;

public class ArbitrageStrategyOrchestratorTests
{
    private const string Symbol = "BTC/USDT:USDT";

    private static CoinDataModel CoinData()
    {
        var rateA = new ExchangeRateModel
        {
            Symbol = Symbol,
            Exchange = "binance-orch",
            ExchangeRate = 100,
            structOrderBook = OrderBookBuilder.Build(bids: new[] { (99.9, 1000.0) }, asks: new[] { (100.1, 1000.0) }),
            FundingRate = new ccxt.FundingRate { fundingRate = 0.001 },
        };
        var rateB = new ExchangeRateModel
        {
            Symbol = Symbol,
            Exchange = "okx-orch",
            ExchangeRate = 101,
            structOrderBook = OrderBookBuilder.Build(bids: new[] { (100.9, 1000.0) }, asks: new[] { (101.1, 1000.0) }),
            FundingRate = new ccxt.FundingRate { fundingRate = -0.001 },
        };
        return new CoinDataModel { Symbol = Symbol, ExchangeRates = new List<ExchangeRateModel> { rateA, rateB } };
    }

    [Fact]
    public async Task General_AllStrategiesDisabled_CompletesWithoutInvokingAnyCalculator()
    {
        var config = ServiceFactory.BuildConfig();
        var orchestrator = ServiceFactory.BuildOrchestrator(config);

        var act = async () => await orchestrator.General(CoinData());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task General_FuturesEnabled_RunsFuturesPipelineWithoutThrowing()
    {
        var config = ServiceFactory.BuildConfigWithFlags(futures: true, spreadSize: 0.1, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-orch", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-orch", Symbol);
        var orchestrator = ServiceFactory.BuildOrchestrator(config, dataService);

        var act = async () => await orchestrator.General(CoinData());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task General_FundingEnabled_RunsFundingPipelineWithoutThrowing()
    {
        var config = ServiceFactory.BuildConfigWithFlags(funding: true, fundingThreshold: 0.0001, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-orch", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-orch", Symbol);
        var orchestrator = ServiceFactory.BuildOrchestrator(config, dataService);

        var act = async () => await orchestrator.General(CoinData());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task General_AllStrategiesEnabled_RunsAllPipelinesWithoutThrowing()
    {
        var config = ServiceFactory.BuildConfigWithFlags(futures: true, funding: true, spot: true, spreadSize: 0.1, fundingThreshold: 0.0001, positionSize: 1);
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "binance-orch", Symbol);
        await ServiceFactory.RegisterExchangeService(dataService, config, "okx-orch", Symbol);
        var orchestrator = ServiceFactory.BuildOrchestrator(config, dataService);

        var act = async () => await orchestrator.General(CoinData());

        await act.Should().NotThrowAsync();
    }
}
