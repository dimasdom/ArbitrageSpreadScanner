using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace ArbitrageScanner.Funding.Services
{
    public class FundingObserverService
    {
        private readonly DataService _dataService;
        private readonly ConfigModel _config;
        private readonly FundingPositionCalculatorService _fundingPositionCalculatorService;
        private readonly IServicesCommunicationService _servicesCommunicationService;
        private readonly UserInterfaceService _userInterfaceService;

        private const int PositionWatchTimeoutSeconds = 120;
        private const int WatchLoopDelayMinutes = 5;
        private const int RetryDelaySeconds = 2;

        public FundingObserverService(
            DataService dataService,
            IConfiguration configuration,
            FundingPositionCalculatorService fundingPositionCalculatorService,
            IServicesCommunicationService servicesCommunicationService,
            UserInterfaceService userInterfaceService)
        {
            _dataService = dataService;
            _config = configuration.GetArbitrageConfig();
            _fundingPositionCalculatorService = fundingPositionCalculatorService;
            _servicesCommunicationService = servicesCommunicationService;
            _userInterfaceService = userInterfaceService;
        }

        public async Task WatchPossibleFundingPositionWithCombineKey(TradeOpportunityModel model)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(PositionWatchTimeoutSeconds));
            var exchangeRequest = WatchPossiblePosition(model);
            var completedTask = await Task.WhenAny(exchangeRequest, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Console.WriteLine($"Timeout while watching funding position: {_dataService.GenerateCombineKeyFor(model)}");
                return;
            }
            var updatedPossiblePosition = exchangeRequest.Result;
            if (updatedPossiblePosition is null)
                return;
            var combineKey = _dataService.GenerateCombineKeyFor(model);
            updatedPossiblePosition.Type = SpreadType.Funding;

            var keepWatching = updatedPossiblePosition.Spread > _config.KeepWatchingSpread;


            if (keepWatching)
            {
                combineKey = _dataService.GenerateCombineKeyFor(updatedPossiblePosition);
                _dataService.WatchListFunding[combineKey] = updatedPossiblePosition;
                updatedPossiblePosition.ActionType = OrderStatus.Update;
                try
                {
                    await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
                    await _dataService.UpdateActivePossiblePositionAsync(updatedPossiblePosition);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update possible position text message: {combineKey}, Exception: {ex.Message}");
                }
                Console.WriteLine($"Updated funding position: {combineKey}");
            }
            else
            {
                updatedPossiblePosition.ActionType = OrderStatus.Close;
                _dataService.WatchListFunding.TryRemove(combineKey, out var _);
                try
                {
                    await _dataService.DeleteActivePossiblePositionAsync(updatedPossiblePosition);
                    await _userInterfaceService.PostInvalidatingSpreadToTelegram(updatedPossiblePosition);
                    await _servicesCommunicationService.PostPossiblePosition(updatedPossiblePosition);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to post possible position text message: {combineKey}, Exception: {ex.Message}");
                }
            }
        }
        public async Task StartToWatchPositionsWithCombineKeys()
        {
            while (true)
            {
                try
                {
                    if (_dataService.WatchListFunding.Any())
                    {
                        var watchTasks = _dataService.WatchListFunding.Select(item => WatchPossibleFundingPositionWithCombineKey(item.Value));
                        await Task.WhenAll(watchTasks);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(WatchLoopDelayMinutes));
                }
                catch (Exception ex)
                {
                    _dataService.LogErrorEntry(ex, method: "StartToWatchPositionsWithCombineKeys");
                    Console.WriteLine(ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
                }
            }
        }
        public async Task CheckAndAddNewFuturesPositionsToWatch(List<TradeOpportunityModel> possiblePositions)
        {
            if (possiblePositions != null && possiblePositions.Any())
            {
                foreach(var tradeOpportunity in possiblePositions)
                {
                    var combineKey = _dataService.GenerateCombineKeyFor(tradeOpportunity);
                    if (!_dataService.WatchListFunding.Keys.Contains(combineKey))
                    {
                        tradeOpportunity.Guid = Guid.NewGuid();
                        tradeOpportunity.Type = SpreadType.Funding;
                        tradeOpportunity.ActionType = OrderStatus.Open;
                        tradeOpportunity.DateTime = DateTime.Now;
                        tradeOpportunity.StartSpread = tradeOpportunity.Spread;
                        var updatedPossiblePosition = await WatchPossiblePosition(tradeOpportunity);
                        if (updatedPossiblePosition is null)
                            continue;
                        _dataService.WatchListFunding.TryAdd(combineKey, updatedPossiblePosition);
                        await _dataService.AddActivePossiblePositionAsync(tradeOpportunity);
                        await _servicesCommunicationService.PostPossiblePosition(tradeOpportunity);
                        await _userInterfaceService.PostFoundFundingSpreadToTelegram(tradeOpportunity);
                        await _dataService.SaveFoundFundingSpreadAsync(tradeOpportunity);
                    }
                }
            }
        }
        public async Task<TradeOpportunityModel?> WatchPossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            var exchangeFundingA = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateA!.Exchange].GetFundingInfo(tradeOpportunity.ExchangeRateA!.Symbol);
            var exchangeFundingB = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateB!.Exchange].GetFundingInfo(tradeOpportunity.ExchangeRateA!.Symbol);
            var exchangeRateA = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateA!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeRateA!.Symbol, true, 50, onlyFutures: true);
            var exchangeRateB = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateB!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeRateA!.Symbol, true, 50, onlyFutures: true);

            if (!exchangeFundingA.HasValue || !exchangeFundingB.HasValue
                || !exchangeFundingA.Value.fundingRate.HasValue || !exchangeFundingB.Value.fundingRate.HasValue
                || exchangeRateA is null || exchangeRateB is null)
                return null;

            var spread = _fundingPositionCalculatorService.CalculateFundingFor(exchangeFundingA.Value.fundingRate.Value, exchangeFundingB.Value.fundingRate.Value, out var isLong);
            tradeOpportunity.Spread = spread;
            tradeOpportunity.ExchangeRateA = exchangeRateA;
            tradeOpportunity.ExchangeRateB = exchangeRateB;
            tradeOpportunity.ExchangeRateA.FundingRateValue = exchangeFundingA.Value.fundingRate.Value;
            tradeOpportunity.ExchangeRateB.FundingRateValue = exchangeFundingB.Value.fundingRate.Value;
            tradeOpportunity.ExchangeLong = isLong ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
            tradeOpportunity.ExchangeShort = isLong ? tradeOpportunity.ExchangeRateB : tradeOpportunity.ExchangeRateA;
            return tradeOpportunity;
        }
        public DateTime GetNextPayoutUtc(string interval)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(interval))
                {
                    return default;
                }
                TimeSpan total = ParseInterval(interval);
                DateTime now = DateTime.UtcNow;

                long ticks = now.Ticks;
                long intervalTicks = total.Ticks;
                long nextTicks = ((ticks / intervalTicks) + 1) * intervalTicks;

                return new DateTime(nextTicks, DateTimeKind.Utc);
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex);
            }
            return default;
        }
        internal TimeSpan ParseInterval(string interval)
        {
            var regex = new Regex(@"((\d+)h)?\s*((\d+)m)?", RegexOptions.IgnoreCase);
            var match = regex.Match(interval.Trim());

            if (!match.Success)
                throw new ArgumentException("Неверный формат интервала. Пример: '8h', '45m', '1h30m'");

            int hours = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            int minutes = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;

            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        }
    }
}
