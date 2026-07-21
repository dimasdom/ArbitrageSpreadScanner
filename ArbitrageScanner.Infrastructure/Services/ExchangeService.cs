using ccxt;
using ccxt.pro;
using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ExchangeService : ExchangePairService
    {
        private readonly DataService _dataService;
        private readonly ConfigModel _config;

        public ExchangeService(DataService dataService, IConfiguration configuration)
        {
            _dataService = dataService;
            _config = configuration.GetArbitrageConfig();
        }

        public Exchange? exchange;
        public Dictionary<string, MarketInterface> markets { get; private set; } = new Dictionary<string, MarketInterface>();
        public Dictionary<string, MarketInterface> spotMarkets { get; private set; } = new Dictionary<string, MarketInterface>();
        public ConcurrentDictionary<string, (Ticker ticker, IOrderBook orderbook)> watchedPairs = new ConcurrentDictionary<string, (Ticker ticker, IOrderBook orderbook)>();

        private const int SpotFetchTimeoutSeconds = 20;
        public string GetExchangeName()
        {
            return exchange!.name?.ToString() ?? "";
        }
        public Exchange GetExchange()
        {
            return exchange!;
        }
        public async Task<double> GetActualRate(string symbol)
        {
            try
            {
                var ticker = await exchange!.FetchTicker(symbol);
                if (ticker.last.HasValue)
                    return ticker.last.Value;
                else
                    return 0;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, symbol, "GetActualRate", exchange?.name?.ToString() ?? "");
                return 0;
            }
        }
        public async Task<ExchangeRateModel?> GetDataForCoin(string symbol, bool checkLiquidity = true, int timeoutSeconds = 30, bool onlyFutures = false)
        {
            try
            {
                ExchangeRateModel? res = null;
                if (markets.ContainsKey(symbol) || spotMarkets.ContainsKey(symbol))
                {
                    var needLiquidity = _config.PositionSize * 5;
                    Ticker? ticker = null;
                    ccxt.OrderBook? orderBook = null;
                    ccxt.FundingRate? fundingrate = null;
                    Ticker? spotTicker = null;
                    ccxt.OrderBook? spotOrderBook = null;

                    // Spot symbols (BTC/USDT) have no colon; futures symbols (BTC/USDT:USDT) do.
                    bool isSpotSymbol = !symbol.Contains(':');
                    var spotSymbol = symbol.Replace(":USDT", "");

                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                    var mainRequest = Task.Run(async () =>
                    {
                        try
                        {
                            ticker = await exchange!.FetchTicker(symbol);
                            // Use symbol directly — correct for both spot (BTC/USDT) and futures (BTC/USDT:USDT)
                            orderBook = await exchange!.FetchOrderBook(symbol);
                            if (markets.ContainsKey(symbol))
                                fundingrate = await exchange!.FetchFundingRate(symbol);
                        }
                        catch (Exception ex)
                        {
                            _dataService.LogErrorEntry(ex, symbol, "GetDataForCoin-ExchangeRequest", exchange?.name?.ToString() ?? "");
                            Console.WriteLine(ex.Message);
                        }
                    });

                    // Spot fetch runs in parallel with the main request.
                    // Skipped when the symbol is already spot — reuse main ticker/book instead.
                    Task spotRequest = Task.CompletedTask;
                    if (!onlyFutures && !isSpotSymbol && _config.Spot && spotMarkets.ContainsKey(spotSymbol))
                    {
                        spotRequest = Task.Run(async () =>
                        {
                            try
                            {
                                var spotTickerTask = exchange!.FetchTicker(spotSymbol);
                                var spotOrderBookTask = exchange!.FetchOrderBook(spotSymbol);
                                // .ContinueWith(_ => { }) observes any exception from WhenAll so it does not
                                // surface as an UnobservedTaskException when the exchange (e.g. BingX) throws.
                                await Task.WhenAny(
                                    Task.WhenAll(spotTickerTask, spotOrderBookTask).ContinueWith(_ => { }),
                                    Task.Delay(TimeSpan.FromSeconds(SpotFetchTimeoutSeconds)));
                                if (spotTickerTask.IsCompletedSuccessfully)
                                    spotTicker = spotTickerTask.Result;
                                if (spotOrderBookTask.IsCompletedSuccessfully)
                                    spotOrderBook = spotOrderBookTask.Result;
                            }
                            catch (Exception ex)
                            {
                                _dataService.LogErrorEntry(ex, spotSymbol, "GetDataForCoin-SpotRequest", exchange?.name?.ToString() ?? "");
                                Console.WriteLine(ex.Message);
                            }
                        });
                    }

                    var completedTask = await Task.WhenAny(mainRequest, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        Console.WriteLine($"Timeout: {symbol}|{exchange?.name?.ToString()} took too long and was cancelled.");
                        return null;
                    }

                    // Spot has an internal SpotFetchTimeoutSeconds, but also respect the overall timeout
                    // so slow spot fetches never push total time past timeoutSeconds.
                    await Task.WhenAny(spotRequest, timeoutTask);

                    if (ticker.HasValue &&
                        ticker.Value.last.HasValue &&
                        orderBook.HasValue &&
                        orderBook.Value.bids != null &&
                        orderBook.Value.asks != null)
                    {
                        var liquidity = GetLiquidity(orderBook.Value, ticker.Value.last.Value);
                        if ((checkLiquidity && liquidity.askLiquid > needLiquidity && liquidity.bidLiquid > needLiquidity) || !checkLiquidity)
                        {
                            res = new ExchangeRateModel
                            {
                                Symbol = symbol,
                                Exchange = exchange?.name?.ToString() ?? "",
                                ExchangeRate = ticker.Value.last.Value,
                                VolumeAsk = liquidity.askLiquid,
                                VolumeBid = liquidity.bidLiquid,
                                FundingRate = fundingrate,
                                FundingRateValue = fundingrate.HasValue ? fundingrate.Value.fundingRate.HasValue ? fundingrate.Value.fundingRate.Value : 0 : 0,
                                structOrderBook = orderBook.Value,
                                Ticker = ticker.Value
                            };

                            if (isSpotSymbol)
                            {
                                // Already a spot symbol — reuse the main fetch data, no extra round trip needed.
                                res.SpotTicker = new ExchangeRateModel
                                {
                                    Symbol = symbol,
                                    Exchange = exchange?.name?.ToString() ?? "",
                                    ExchangeRate = ticker.Value.last.Value,
                                    VolumeAsk = liquidity.askLiquid,
                                    VolumeBid = liquidity.bidLiquid,
                                    structOrderBook = orderBook.Value,
                                    Ticker = ticker.Value,
                                };
                            }
                            else if (spotTicker.HasValue && spotOrderBook.HasValue && spotTicker.Value.last.HasValue)
                            {
                                var spotLiquidity = GetLiquidity(spotOrderBook.Value, spotTicker.Value.last.Value);
                                res.SpotTicker = new ExchangeRateModel
                                {
                                    Symbol = spotSymbol,
                                    Exchange = exchange?.name?.ToString() ?? "",
                                    ExchangeRate = spotTicker.Value.last.Value,
                                    VolumeAsk = spotLiquidity.askLiquid,
                                    VolumeBid = spotLiquidity.bidLiquid,
                                    structOrderBook = spotOrderBook.Value,
                                    Ticker = spotTicker.Value,
                                };
                            }
                        }
                    }
                }
                return res;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, symbol, "GetDataForCoin", exchange?.name?.ToString() ?? "");
                Console.WriteLine($"Exception: {symbol}|{exchange?.name?.ToString()}:{ex.Message}");
                return null;
            }
        }

        
        public async Task<FundingRate?> GetFundingInfo(string symbol) {
            try
            {
                var fundingInfo = await exchange!.FetchFundingRate(symbol);
                return fundingInfo;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, symbol, "GetFundingInfo", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
            }
            return null;
        }

       

        public async Task<IEnumerable<string>> LoadSwapPairs()
        {
            try
            {
            var allData = await exchange!.LoadMarkets();
            var usdPairs = new List<string>();
            foreach (var usdPair in allData.Keys)
            {
                if (allData[usdPair].swap == true)
                    usdPairs.Add(usdPair);
            }
            return usdPairs;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "LoadSwapPairs", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
                return Enumerable.Empty<string>();
            }
        }

        public async Task LoadSwapMarkets()
        {
            try
            {
            if (!_dataService.ExchangeMarkets.Keys.Contains(GetExchangeName()))
            {
                _dataService.ExchangeMarkets.TryAdd(GetExchangeName(), new Dictionary<string, MarketInterface>());
                var allData = await exchange!.LoadMarkets();
                foreach (var usdPair in allData.Keys)
                {
                    if (usdPair.EndsWith(":USDT") && allData[usdPair].swap == true)
                    {
                        _dataService.ExchangeMarkets[GetExchangeName()][usdPair] = allData[usdPair];
                        markets[usdPair] = allData[usdPair];
                    }
                        
                }
            }
            else
            {
                foreach (var pair in _dataService.ExchangeMarkets[GetExchangeName()])
                {
                    markets[pair.Key]=pair.Value;
                }
            }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "LoadSwapMarkets", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
            }
        }

        public async Task<IEnumerable<string>> LoadSpotPairs()
        {
            try
            {
            var allData = await exchange!.LoadMarkets();
            var usdPairs = new List<string>();
            foreach (var usdPair in allData.Keys)
            {
                if (usdPair.EndsWith("/USDT") && allData[usdPair].spot == true)
                    usdPairs.Add(usdPair);
            }
            return usdPairs;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "LoadSpotPairs", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
                return Enumerable.Empty<string>();
            }
        }
        public async Task UpdatePairs()
        {
            try
            {
            var allData = await exchange!.LoadMarkets();
            foreach (var usdPair in allData.Keys)
            {
                if (allData[usdPair].swap == true)
                {
                    if (!_dataService.ExchangeMarkets[GetExchangeName()].ContainsKey(usdPair))
                    {
                        _dataService.ExchangeMarkets[GetExchangeName()][usdPair] = allData[usdPair];
                        markets[usdPair] = allData[usdPair];
                    }
                }
            }
            foreach (var usdPair in allData.Keys)
            {
                if (allData[usdPair].spot == true)
                {
                    if (!_dataService.ExchangeSpotMarkets[GetExchangeName()].ContainsKey(usdPair))
                    {
                        _dataService.ExchangeSpotMarkets[GetExchangeName()][usdPair] = allData[usdPair];
                        spotMarkets[usdPair] = allData[usdPair];
                    }
                }
            }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "UpdatePairs", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
            }
        }
        public async Task LoadSpotMarkets()
        {
            try
            {
            if (!_dataService.ExchangeSpotMarkets.Keys.Contains(GetExchangeName()))
            {
                _dataService.ExchangeSpotMarkets.TryAdd(GetExchangeName(), new Dictionary<string, MarketInterface>());
                var allData = await exchange!.LoadMarkets();
                foreach (var usdPair in allData.Keys)
                {
                    if (allData[usdPair].spot == true)
                    {
                        _dataService.ExchangeSpotMarkets[GetExchangeName()][usdPair] = allData[usdPair];
                        spotMarkets[usdPair] = allData[usdPair];
                    }
                }
            }
            else
            {
                foreach (var pair in _dataService.ExchangeSpotMarkets[GetExchangeName()])
                {
                    spotMarkets[pair.Key] = pair.Value;
                }
            }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "LoadSpotMarkets", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
            }
        }

        public Task Init<T>(T Exchange) where T : Exchange
        {
            try
            {
                exchange = Exchange;
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, "", "Init", exchange?.name?.ToString() ?? "");
                Console.WriteLine(ex.Message);
            }

            return Task.CompletedTask;
        }
    }

}

