using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Spot.Services;
using Microsoft.Extensions.Configuration;

namespace ArbitrageScanner.Worker.Controllers
{
    public class ArbitrageStrategyOrchestrator
    {
        private readonly ConfigModel _config;
        private readonly FuturesPositionCalculatorService _futuresPositionCalculatorService;
        private readonly FundingPositionCalculatorService _fundingPositionCalculatorService;
        private readonly SpotPositionCalculatorService _spotPositionCalculatorService;
        private readonly FuturesObserverService _futuresObserverService;
        private readonly FundingObserverService _fundingObserverService;
        private readonly SpotObserverService _spotObserverService;

        public ArbitrageStrategyOrchestrator(
            IConfiguration configuration,
            FuturesPositionCalculatorService futuresPositionCalculatorService,
            FundingPositionCalculatorService fundingPositionCalculatorService,
            SpotPositionCalculatorService spotPositionCalculatorService,
            FuturesObserverService futuresObserverService,
            FundingObserverService fundingObserverService,
            SpotObserverService spotObserverService)
        {
            _config = configuration.GetArbitrageConfig();
            _futuresPositionCalculatorService = futuresPositionCalculatorService;
            _fundingPositionCalculatorService = fundingPositionCalculatorService;
            _spotPositionCalculatorService = spotPositionCalculatorService;
            _futuresObserverService = futuresObserverService;
            _fundingObserverService = fundingObserverService;
            _spotObserverService = spotObserverService;
        }

        public async Task General(CoinDataModel coinData)
        {
            var computeTasks = new List<Task>();
            if (_config.Futures)
            {
                computeTasks.Add(FindAndComputeFuturesPositionsList(CoinDataModel.DeepClone(coinData)));
            }
            if (_config.Funding)
            {
                computeTasks.Add(FindAndComputeFundingPositionsList(CoinDataModel.DeepClone(coinData)));
            }
            if (_config.Spot)
            {
                computeTasks.Add(FindAndComputeSpotPositions(CoinDataModel.DeepClone(coinData)));
            }

            await Task.WhenAll(computeTasks);
        }

        public async Task FindAndComputeFuturesPositionsList(CoinDataModel coinData)
        {
            var possiblePositions = await _futuresPositionCalculatorService.FindPossiblePositionsForExchangeSet(coinData);
            await _futuresObserverService.CheckAndAddNewFuturesPositionsToWatch(possiblePositions);
        }

        public async Task FindAndComputeFundingPositionsList(CoinDataModel coinData)
        {
            var possiblePositions = await _fundingPositionCalculatorService.FindPossiblePositionsForExchangeSet(coinData);
            await _fundingObserverService.CheckAndAddNewFuturesPositionsToWatch(possiblePositions);
        }

        public async Task FindAndComputeSpotPositions(CoinDataModel coinData)
        {
            var tradeOpportunity = await _spotPositionCalculatorService.FindPossiblePositionsForExchangeSet(coinData);
            await _spotObserverService.CheckAndAddNewFuturesPositionsToWatch(tradeOpportunity);
        }
    }
}
