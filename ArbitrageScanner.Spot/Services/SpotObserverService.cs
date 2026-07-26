using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ArbitrageScanner.Spot.Services
{
    public class SpotObserverService
    {
        private readonly DataService _dataService;
        private readonly ConfigModel _config;
        private readonly SpotPositionCalculatorService _spotPositionCalculatorService;
        private readonly IServicesCommunicationService _servicesCommunicationService;
        private readonly UserInterfaceService _userInterfaceService;
        private readonly SemaphoreSlim _semaphore = new(5, 5);
        private readonly ConcurrentDictionary<string, byte> _inFlightKeys = new();

        private const int WatchLoopDelayMs = 1000;
        private const int RetryDelaySeconds = 2;

        public SpotObserverService(
            DataService dataService,
            IConfiguration configuration,
            SpotPositionCalculatorService spotPositionCalculatorService,
            IServicesCommunicationService servicesCommunicationService,
            UserInterfaceService userInterfaceService)
        {
            _dataService = dataService;
            _config = configuration.GetArbitrageConfig();
            _spotPositionCalculatorService = spotPositionCalculatorService;
            _servicesCommunicationService = servicesCommunicationService;
            _userInterfaceService = userInterfaceService;
        }

        public async Task WatchPossibleSpotPositionWithCombineKey(TradeOpportunityModel model)
        {
            try
            {
            double spread = model.Spread;
            var updatedPossiblePosition = await _spotPositionCalculatorService.WatchPossiblePosition(model);
            if (updatedPossiblePosition is null)
                return;
            string combineKey = _dataService.GenerateCombineKeyFor(model);
            bool keepWatching = false;

            keepWatching = updatedPossiblePosition.Spread > _config.KeepWatchingSpread;
            

            if (keepWatching)
            {
                _dataService.WatchListSpot[combineKey] = updatedPossiblePosition;
                updatedPossiblePosition.Type = SpreadType.Spot;
                updatedPossiblePosition.ActionType = OrderStatus.Update;
                await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
                await _dataService.UpdateActivePossiblePositionAsync(updatedPossiblePosition);
            }
            else
            {
                _dataService.WatchListSpot.TryRemove(combineKey, out var _);
                await _dataService.DeleteActivePossiblePositionAsync(updatedPossiblePosition);
                await _userInterfaceService.PostInvalidatingSpreadToTelegram(updatedPossiblePosition);
                updatedPossiblePosition.ActionType = OrderStatus.Close;
                await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
            }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, model.ExchangeLong?.Symbol ?? "", "WatchPossibleSpotPositionWithCombineKey", model.ExchangeRateA!.Exchange);
                Console.WriteLine(ex.Message);
            }
        }

        public async Task StartToWatchPositionsWithCombineKeys(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Dispatch each key independently instead of awaiting the whole batch: a single
                    // slow/stuck item must not delay reprocessing of the rest of the watchlist on the
                    // next tick. _inFlightKeys keeps a key from being scheduled twice while its
                    // previous watch is still running.
                    foreach (var (combineKey, model) in _dataService.WatchListSpot)
                    {
                        if (_inFlightKeys.TryAdd(combineKey, 0))
                            _ = WatchQueuedPositionAsync(combineKey, model);
                    }
                    await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromMilliseconds(WatchLoopDelayMs), cancellationToken);
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, JsonSerializer.Serialize(_dataService.WatchListSpot), "StartToWatchPositionsWithCombineKeys");
                    Console.WriteLine(ex.Message);
                    await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
                }
            }
        }

        private async Task WatchQueuedPositionAsync(string combineKey, TradeOpportunityModel model)
        {
            await _semaphore.WaitAsync();
            try
            {
                await WatchPossibleSpotPositionWithCombineKey(model);
            }
            finally
            {
                _semaphore.Release();
                _inFlightKeys.TryRemove(combineKey, out _);
            }
        }

        public async Task CheckAndAddNewFuturesPositionsToWatch(List<TradeOpportunityModel> possiblePositions)
        {
            if (possiblePositions != null && possiblePositions.Any())
            {
                foreach (var tradeOpportunity in possiblePositions)
                {
                    string combineKey = _dataService.GenerateCombineKeyFor(tradeOpportunity);
                    if (!_dataService.WatchListSpot.Keys.Contains(combineKey))
                    {
                        tradeOpportunity.Guid = Guid.NewGuid();
                        tradeOpportunity.Type = SpreadType.Spot;
                        tradeOpportunity.ActionType = OrderStatus.Open;
                        tradeOpportunity.StartSpread = tradeOpportunity.Spread;
                        _dataService.WatchListSpot.TryAdd(combineKey, tradeOpportunity);
                        await _dataService.AddActivePossiblePositionAsync(tradeOpportunity);
                        await _servicesCommunicationService.PostPossiblePosition(tradeOpportunity);
                        await _userInterfaceService.PostFoundSpotSpreadToTelegram(tradeOpportunity);
                        await _dataService.SaveFoundSpreadAsync(tradeOpportunity);
                    }
                }
            }
        }


    }
}
