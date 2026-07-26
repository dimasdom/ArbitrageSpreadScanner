using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ArbitrageScanner.Futures.Services
{
    public class FuturesObserverService
    {
        private readonly DataService _dataService;
        private readonly ConfigModel _config;
        private readonly FuturesPositionCalculatorService _futuresPositionCalculatorService;
        private readonly IServicesCommunicationService _servicesCommunicationService;
        private readonly UserInterfaceService _userInterfaceService;
        private readonly SemaphoreSlim _semaphore = new(5, 5);
        private readonly ConcurrentDictionary<string, byte> _inFlightKeys = new();

        private const int WatchLoopDelayMs = 1000;
        private const int RetryDelaySeconds = 2;

        public FuturesObserverService(
            DataService dataService,
            IConfiguration configuration,
            FuturesPositionCalculatorService futuresPositionCalculatorService,
            IServicesCommunicationService servicesCommunicationService,
            UserInterfaceService userInterfaceService)
        {
            _dataService = dataService;
            _config = configuration.GetArbitrageConfig();
            _futuresPositionCalculatorService = futuresPositionCalculatorService;
            _servicesCommunicationService = servicesCommunicationService;
            _userInterfaceService = userInterfaceService;
        }

        public async Task WatchPossibleFuturesPositionWithCombineKey(TradeOpportunityModel model)
        {
            try
            {
            double spread = model.Spread;
            var updatedPossiblePosition = await _futuresPositionCalculatorService.WatchPossiblePosition(model);
            if (updatedPossiblePosition is null)
                return;
            var combineKey = _dataService.GenerateCombineKeyFor(model);
            bool keepWatching = false;

            if (spread > 0)
            {
                keepWatching = updatedPossiblePosition.Spread > _config.KeepWatchingSpread;
            }
            else
            {
                keepWatching = updatedPossiblePosition.Spread < -_config.KeepWatchingSpread;
            }

            if (keepWatching)
            {
                _dataService.WatchList[combineKey] = updatedPossiblePosition;
                updatedPossiblePosition.ActionType = OrderStatus.Update;
                await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
                await _dataService.UpdateActivePossiblePositionAsync(updatedPossiblePosition);
            }
            else
            {
                _dataService.WatchList.TryRemove(combineKey, out var _);
                updatedPossiblePosition.ActionType = OrderStatus.Close;
                await _userInterfaceService.PostInvalidatingSpreadToTelegram(updatedPossiblePosition);
                await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
                await _dataService.DeleteActivePossiblePositionAsync(updatedPossiblePosition);
            }
        }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, model.ExchangeRateA?.Symbol ?? "", "WatchPossibleFuturesPositionWithCombineKey");
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
                    foreach (var (combineKey, model) in _dataService.WatchList)
                    {
                        if (_inFlightKeys.TryAdd(combineKey, 0))
                            _ = WatchQueuedPositionAsync(combineKey, model);
                    }
                    await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromMilliseconds(WatchLoopDelayMs), cancellationToken);
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, JsonSerializer.Serialize(_dataService.WatchList), "StartToWatchPositionsWithCombineKeys");
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
                await WatchPossibleFuturesPositionWithCombineKey(model);
            }
            finally
            {
                _semaphore.Release();
                _inFlightKeys.TryRemove(combineKey, out _);
            }
        }

        public async Task CheckAndAddNewFuturesPositionToWatch(TradeOpportunityModel tradeOpportunity)
        {
            if (tradeOpportunity is null || _dataService.WatchList.Keys.Contains(tradeOpportunity.ExchangeRateA!.Symbol))
                return;

            tradeOpportunity.Guid = Guid.NewGuid();
            _dataService.WatchList.TryAdd(tradeOpportunity.ExchangeRateA!.Symbol, tradeOpportunity);
            await _dataService.AddActivePossiblePositionAsync(tradeOpportunity);
            await _servicesCommunicationService.PostPossiblePosition(tradeOpportunity);
            await _userInterfaceService.PostFoundSpreadToTelegram(tradeOpportunity);
            await _dataService.SaveFoundSpreadAsync(tradeOpportunity);
        }
        public async Task CheckAndAddNewFuturesPositionsToWatch(List<TradeOpportunityModel> possiblePositions)
        {
            if (possiblePositions != null && possiblePositions.Any())
            {
                foreach (var tradeOpportunity in possiblePositions)
                {
                    var combineKey = _dataService.GenerateCombineKeyFor(tradeOpportunity);
                    if (!_dataService.WatchList.Keys.Contains(combineKey))
                    {
                        tradeOpportunity.Guid = Guid.NewGuid();
                        tradeOpportunity.Type = SpreadType.Futures;
                        tradeOpportunity.ActionType = OrderStatus.Open;
                        tradeOpportunity.StartSpread = tradeOpportunity.Spread;
                        _dataService.WatchList.TryAdd(combineKey, tradeOpportunity);
                        try
                        {
                        await _dataService.AddActivePossiblePositionAsync(tradeOpportunity);
                        await _servicesCommunicationService.PostPossiblePosition(tradeOpportunity);
                        await _userInterfaceService.PostFoundSpreadToTelegram(tradeOpportunity);
                        await _dataService.SaveFoundSpreadAsync(tradeOpportunity);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to post possible position text message: {combineKey}, Exception: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
