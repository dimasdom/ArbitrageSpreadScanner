using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ccxt;
using ArbitrageScanner.Infrastructure.Common;
using MongoDB.Bson;
using System.Collections.Concurrent;
using MongoDB.Bson.Serialization.Conventions;
using Azure.Storage.Blobs;
using MongoDB.Driver.Core.Configuration;
using Azure.Storage.Blobs.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text.Json;
using System.Reflection.Metadata.Ecma335;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
namespace ArbitrageScanner.Infrastructure.Services
{
    public class DataService
    {
        private readonly ITradeOpportunityRepository _tradeOpportunityRepositoryMongoRepository;
        private readonly ExchangeRegistry _exchangeRegistry;
        private static readonly StrategyWatchListService _futuresWatchListService = new();
        private static readonly StrategyWatchListService _fundingWatchListService = new();
        private static readonly StrategyWatchListService _spotWatchListService = new();
        private static readonly ProxyPool _proxyPool = new();

        public DataService(ITradeOpportunityRepository tradeOpportunityRepositoryMongoRepository, IConfiguration configuration)
        {
            var config = configuration.GetSection("Arbitrage").Get<ConfigModel>() ?? new ConfigModel();
            _tradeOpportunityRepositoryMongoRepository = tradeOpportunityRepositoryMongoRepository;
            _exchangeRegistry = new ExchangeRegistry(config.ExchangeList);
        }

        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> exchangeMarkets => _exchangeRegistry.ExchangeMarkets;
        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> exchangeSpotMarkets => _exchangeRegistry.ExchangeSpotMarkets;
        public List<string> exchangeInitList => _exchangeRegistry.ExchangeInitList;
        public ConcurrentDictionary<string, Exchange> exchanges => _exchangeRegistry.Exchanges;
        public ConcurrentDictionary<string, ExchangeService> exchangeServices => _exchangeRegistry.ExchangeServices;
        public ConcurrentDictionary<string, ExchangeService> exchangeObserverServices => _exchangeRegistry.ExchangeObserverServices;
        public static ConcurrentDictionary<string, TradeOpportunityModel> watchList => _futuresWatchListService.Items;
        public static ConcurrentDictionary<string, TradeOpportunityModel> watchListFunding => _fundingWatchListService.Items;
        public static ConcurrentDictionary<string, TradeOpportunityModel> watchListSpot => _spotWatchListService.Items;

        public static List<ProxyModel> ProxiesList
        {
            get { return _proxyPool.Proxies; }
        }

        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> ExchangeMarkets => exchangeMarkets;
        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> ExchangeSpotMarkets => exchangeSpotMarkets;
        public List<string> ExchangeInitList => exchangeInitList;
        public ConcurrentDictionary<string, Exchange> Exchanges => exchanges;
        public ConcurrentDictionary<string, ExchangeService> ExchangeServices => exchangeServices;
        public ConcurrentDictionary<string, ExchangeService> ExchangeObserverServices => exchangeObserverServices;
        public ConcurrentDictionary<string, TradeOpportunityModel> WatchList => watchList;
        public ConcurrentDictionary<string, TradeOpportunityModel> WatchListFunding => watchListFunding;
        public ConcurrentDictionary<string, TradeOpportunityModel> WatchListSpot => watchListSpot;
        public List<ProxyModel> Proxies => ProxiesList;

        public Task<IEnumerable<string>> GetUniqueCommonFuturesPairsFromApiAsync() => GetUniqueCommonFUturesPairsFromAPI();
        public void LogErrorEntry(Exception ex, string symbol = "", string method = "", string exchange = "") => LogError(ex, symbol, method, exchange);
        public Task LoadProxiesAsync() => LoadProxies();
        public Task LoadActivePossiblePositionsAsync() => LoadActivePossiblePositions();
        public Task AddActivePossiblePositionAsync(TradeOpportunityModel tradeOpportunity) => AddActivePossiblePosition(tradeOpportunity);
        public Task DeleteActivePossiblePositionAsync(TradeOpportunityModel tradeOpportunity) => DeleteActivePossiblePosition(tradeOpportunity);
        public Task UpdateActivePossiblePositionAsync(TradeOpportunityModel tradeOpportunity) => UpdateActivePossiblePosition(tradeOpportunity);
        public Task SaveSpreadsTickerAsync(TradeOpportunityModel tradeOpportunity) => SaveSpreadsTicker(tradeOpportunity);
        public Task SaveFoundSpreadAsync(TradeOpportunityModel tradeOpportunity) => SaveFoundSpread(tradeOpportunity);
        public Task SaveFoundFundingSpreadAsync(TradeOpportunityModel tradeOpportunity) => SaveFoundFundingSpread(tradeOpportunity);
        public Task SaveFoundSpotSpreadAsync(TradeOpportunityModel tradeOpportunity) => SaveFoundSpotSpread(tradeOpportunity);
        public Task SaveSpotSpreadsTickerToMongoAsync(TradeOpportunityModel tradeOpportunity) => SaveSpotSpreadsTickerToMongo(tradeOpportunity);
        public string GenerateCombineKeyFor(TradeOpportunityModel tradeOpportunity) => GenerateCombineKey(tradeOpportunity);
        
        public async Task<IEnumerable<string>> GetUniqueCommonFUturesPairsFromAPI()
        {
            int nodeCount = int.Parse(Environment.GetEnvironmentVariable("NODE_TOTAL") ?? "1");
            int nodeIndex = int.Parse(Environment.GetEnvironmentVariable("NODE_INDEX") ?? "0");
            Dictionary<string, List<string>> pairsPerExchange = new Dictionary<string, List<string>>();
            foreach (var exchangeService in exchangeServices)
            {
                pairsPerExchange[exchangeService.Value.GetExchangeName()] = (await exchangeService.Value.LoadSwapPairs()).ToList();
            }

            List<string> allPairs = pairsPerExchange.First().Value;

            var keys = pairsPerExchange.Keys.ToList();
            for (var i = 1; i < pairsPerExchange.Count; i++)
            {
                allPairs.AddRange(pairsPerExchange[keys[i]]);
            }


            IEnumerable<string> uniqueCommonPairs = allPairs
                .GroupBy(pair => pair)
                .Where(group => group.Count() >= 5)
                .Select(group => group.Key);
            var myPart = uniqueCommonPairs
            .Where((_, i) => i % nodeCount == nodeIndex)
            .ToList();
            return myPart;
        }
        public void LogError(Exception ex, string symbol = "", string method = "", string exchange = "")
        {
            _tradeOpportunityRepositoryMongoRepository.SaveError(ex,symbol,method,exchange);
        }
        public async Task LoadProxies()
        {
            var proxies = await _tradeOpportunityRepositoryMongoRepository.LoadProxies();
            _proxyPool.ReplaceAll(proxies);
        }
        public async Task LoadActivePossiblePositions()
        {
            var activePositions = await _tradeOpportunityRepositoryMongoRepository.GetActivePossiblePositions();
            foreach (var position in activePositions)
            {
                var posPosition = MapTickerToPossiblePosition(position);
                var combineKey = GenerateCombineKey(posPosition);
                switch (posPosition.Type)
                {
                    case SpreadType.Futures:
                        if (!watchList.Keys.Contains(combineKey))
                        {
                            watchList[combineKey] = posPosition;
                        }
                        break;
                    case SpreadType.Funding:
                        if (!watchListFunding.Keys.Contains(combineKey))
                        {
                            watchListFunding[combineKey] = posPosition;
                        }
                        break;
                    case SpreadType.Spot:
                        if (!watchListSpot.Keys.Contains(combineKey))
                        {
                            watchListSpot[combineKey] = posPosition;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public async Task AddActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.AddActivePossiblePosition(tradeOpportunity);
        }
        public async Task DeleteActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.DeleteActivePossiblePosition(tradeOpportunity);
        }
        public async Task UpdateActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.UpdateActivePossiblePosition(tradeOpportunity);
        }
        public async Task SaveSpreadsTicker(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.SaveSpreadsTicker(tradeOpportunity);
        }
        public async Task SaveFoundSpread(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.SaveFoundSpread(tradeOpportunity);
        }
        public async Task SaveFoundFundingSpread(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.SaveFoundFundingSpread(tradeOpportunity);
        }
        public async Task SaveFoundSpotSpread(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.SaveFoundSpotSpread(tradeOpportunity);
        }
        public async Task SaveSpotSpreadsTickerToMongo(TradeOpportunityModel tradeOpportunity)
        {
            await _tradeOpportunityRepositoryMongoRepository.SaveSpotSpreadsTicker(tradeOpportunity);
        }

        public static string GenerateCombineKey(TradeOpportunityModel tradeOpportunity)
        {
            return $"{tradeOpportunity.ExchangeRateA?.Symbol}{tradeOpportunity.ExchangeRateA?.Exchange} {tradeOpportunity.ExchangeRateB?.Exchange}";
        }

        private static TradeOpportunityModel MapTickerToPossiblePosition(TradeOpportunityTickerModel ticker)
        {
            return new TradeOpportunityModel
            {
                Guid = ticker.Guid,
                ExchangeRateA = new ExchangeRateModel() {Symbol = ticker.Symbol, Exchange=ticker.ExchangeA, ExchangeRate = ticker.RateA},
                ExchangeRateB = new ExchangeRateModel() { Symbol = ticker.Symbol, Exchange = ticker.ExchangeB, ExchangeRate = ticker.RateB },
                Type = ticker.Type,
                Symbol = ticker.Symbol,
                DateTime = ticker.DateTime,
                Spread = ticker.Spread,
                PossibleProfit = ticker.PossibleProfit,
                ExchangeLong = new ExchangeRateModel() { Symbol = ticker.Symbol, Exchange = ticker.ExchangeLong},
                ExchangeShort = new ExchangeRateModel() { Symbol = ticker.Symbol, Exchange = ticker.ExchangeShort}
            };
        }
    }
}
