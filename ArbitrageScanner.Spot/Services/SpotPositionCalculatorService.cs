using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using ccxt.pro;

namespace ArbitrageScanner.Spot.Services
{
    public class SpotPositionCalculatorService
    {
        private readonly ConfigModel _config;
        private readonly DataService _dataService;

        public SpotPositionCalculatorService(IConfiguration configuration, DataService dataService)
        {
            _config = configuration.GetArbitrageConfig();
            _dataService = dataService;
        }

        public double CalculateBasis(double markPrice, double indexPrice)
        {
            return (markPrice - indexPrice) / indexPrice * 100;
        }
        public double CalculateSpreadFor(double priceA, double priceB)
        {
            return (priceA - priceB) / priceB * 100;
        }
        public async Task<List<TradeOpportunityModel>> FindPossiblePositionsForExchangeSet(CoinDataModel coinData)
        {
            List<TradeOpportunityModel> possiblePositions = new List<TradeOpportunityModel>();
            double maxSpread = _config.SpreadSize;

            for (var i = coinData.ExchangeRates.Count - 1; i > 0; i--)
            {
                for (var j = i - 1; j >= 0; j--)
                {
                    // Spread = futures ExchangeRate (short) vs spot order book ask (long/buy).
                    // SpotTicker.ExchangeRate is stale — the real executable spot price is SpotTicker.structOrderBook.asks[0][0].
                    if (coinData.ExchangeRates[i].SpotTicker is not null && coinData.ExchangeRates[j].SpotTicker is not null
                        && coinData.ExchangeRates[i].SpotTicker!.structOrderBook.asks?.Count > 0
                        && coinData.ExchangeRates[j].SpotTicker!.structOrderBook.asks?.Count > 0)
                    {
                        double spread = 0;
                        bool exchangeAIsFutures = false;
                        double spotAskI = coinData.ExchangeRates[i].SpotTicker!.structOrderBook.asks![0][0];
                        double spotAskJ = coinData.ExchangeRates[j].SpotTicker!.structOrderBook.asks![0][0];

                        // Futures on i is higher than spot ask on j → short i, long j
                        if (coinData.ExchangeRates[i].ExchangeRate > spotAskJ)
                        {
                            exchangeAIsFutures = true;
                            spread = CalculateSpreadFor(coinData.ExchangeRates[i].ExchangeRate, spotAskJ);
                        }

                        // Futures on j is higher than spot ask on i → short j, long i
                        if (!exchangeAIsFutures && spread < maxSpread && coinData.ExchangeRates[j].ExchangeRate > spotAskI)
                        {
                            spread = CalculateSpreadFor(coinData.ExchangeRates[j].ExchangeRate, spotAskI);
                        }
                        if (spread > maxSpread && spread < 50)
                        {
                            TradeOpportunityModel tradeOpportunity = new TradeOpportunityModel();
                            tradeOpportunity.ExchangeRateA = coinData.ExchangeRates[i];
                            tradeOpportunity.ExchangeRateB = coinData.ExchangeRates[j];
                            tradeOpportunity.Spread = spread;
                            tradeOpportunity.ExchangeLong = exchangeAIsFutures ? tradeOpportunity.ExchangeRateB : tradeOpportunity.ExchangeRateA;
                            tradeOpportunity.ExchangeShort = exchangeAIsFutures ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
                            tradeOpportunity.ExchangeLong!.ExchangeRate = tradeOpportunity.ExchangeLong!.SpotTicker!.ExchangeRate;
                            tradeOpportunity.ExchangeLong!.structOrderBook = tradeOpportunity.ExchangeLong!.SpotTicker!.structOrderBook;
                            tradeOpportunity.ExchangeLong!.Symbol = tradeOpportunity.ExchangeLong!.Symbol.Replace(":USDT", "");
                            tradeOpportunity.Symbol = tradeOpportunity.ExchangeLong!.Symbol;
                            possiblePositions.Add((await CalculatePossibleProfit(tradeOpportunity))!);
                        }
                    }
                }
                coinData.ExchangeRates.RemoveAt(i);
            }
            //if (tradeOpportunity != null)
            //{
            //    tradeOpportunity.Volatility = await ExchangeService.GetVolatilityForSymbol(coinData.Symbol, tradeOpportunity.ExchangeLong.Exchange);
            //    if (tradeOpportunity.Volatility < 0)
            //    {
            //        tradeOpportunity.Volatility = await ExchangeService.GetVolatilityForSymbol(coinData.Symbol, tradeOpportunity.ExchangeLong.Exchange);
            //    }
            //}
            return possiblePositions;
        }

        public Task<TradeOpportunityModel?> CalculatePossibleProfit(TradeOpportunityModel tradeOpportunity)
        {
            if (tradeOpportunity is not null)
            {
                if (tradeOpportunity.ExchangeLong!.structOrderBook.asks?.Count > 0 && tradeOpportunity.ExchangeLong!.structOrderBook.bids?.Count > 0)
                {
                    tradeOpportunity.ExchangeLong!.SlippageLong = CalculateSlippage(tradeOpportunity.ExchangeLong!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, true);
                    tradeOpportunity.ExchangeLong!.SlippageShort = CalculateSlippage(tradeOpportunity.ExchangeLong!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, false);
                    tradeOpportunity.ExchangeLong!.SummarySlipage = tradeOpportunity.ExchangeLong!.SlippageLong + tradeOpportunity.ExchangeLong!.SlippageShort;
                }
                if (tradeOpportunity.ExchangeShort!.structOrderBook.asks?.Count > 0 && tradeOpportunity.ExchangeShort!.structOrderBook.bids?.Count > 0)
                {
                    tradeOpportunity.ExchangeShort!.SlippageLong = CalculateSlippage(tradeOpportunity.ExchangeShort!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, true);
                    tradeOpportunity.ExchangeShort!.SlippageShort = CalculateSlippage(tradeOpportunity.ExchangeShort!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, false);
                    tradeOpportunity.ExchangeShort!.SummarySlipage = tradeOpportunity.ExchangeShort!.SlippageLong + tradeOpportunity.ExchangeShort!.SlippageShort;
                }
                var summarySlippage = tradeOpportunity.ExchangeLong!.SummarySlipage + tradeOpportunity.ExchangeShort!.SummarySlipage;
                var longEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeLong!.Exchange];
                var shortEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeShort!.Exchange];
                var longMaker = longEchange.spotMarkets.TryGetValue(tradeOpportunity.ExchangeLong!.Symbol, out var longSpotMarket) && longSpotMarket.maker.HasValue ? longSpotMarket.maker.Value : 0;
                var longExchangeMaker = longMaker > 0.001 ? longMaker * 10 : longMaker * 100;
                var shortSymbolKey = tradeOpportunity.ExchangeShort!.Symbol.Contains(":USDT") ? tradeOpportunity.ExchangeShort!.Symbol : tradeOpportunity.ExchangeShort!.Symbol + ":USDT";
                var shortMaker = shortEchange.markets.TryGetValue(shortSymbolKey, out var shortMarket) && shortMarket.maker.HasValue ? shortMarket.maker.Value : 0;
                var shortExchangeMaker = shortMaker > 0.001 ? shortMaker * 10 : shortMaker * 100;
                tradeOpportunity.SummaryTarrif = longExchangeMaker * 2 + shortExchangeMaker * 2;
                tradeOpportunity.PossibleProfit = Math.Abs(tradeOpportunity.Spread) - tradeOpportunity.SummaryTarrif - summarySlippage;
            }
            return Task.FromResult<TradeOpportunityModel?>(tradeOpportunity);
        }
        public static double CalculateSlippage(ccxt.OrderBook orderBook, string symbol, double orderSize, bool isLong)
        {
            var orderBookEntries = isLong ? orderBook.asks : orderBook.bids;
            if (orderBookEntries == null || orderBookEntries.Count == 0)
                throw new Exception("Order book is empty!");
            double bestPrice = orderBookEntries[0][0]; // Лучшая цена
            double filledAmount = 0;
            double totalCost = 0;

            foreach (var entry in orderBookEntries)
            {
                double price = entry[0];
                double volume = entry[1];

                double fill = Math.Min(orderSize - filledAmount, volume);
                totalCost += fill * price;
                filledAmount += fill;

                if (filledAmount >= orderSize)
                    break;
            }

            if (filledAmount < orderSize)
                throw new Exception("Not Enough Liqudidy!");

            double avgFillPrice = totalCost / filledAmount;
            double slippage = (avgFillPrice - bestPrice) / bestPrice * 100;
            return slippage;
        }

        public async Task<TradeOpportunityModel> WatchPossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            var exchangeRateLong = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeLong!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeLong!.Symbol, false, 30);
            var exchangeRateShort = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeShort!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeShort!.Symbol, false, 30);
            if (exchangeRateLong is not null && exchangeRateShort is not null
                && exchangeRateShort.structOrderBook.bids?.Count > 0
                && exchangeRateLong.SpotTicker?.structOrderBook.asks?.Count > 0)
            {
                // IMPORTANT: For spot, always use order book prices — best ask to buy (long), best bid to sell (short).
                // structOrderBook on GetDataForCoin result is always the FUTURES order book.
                // The long (spot) leg must read from SpotTicker.structOrderBook which holds the real spot book.
                double shortBid = exchangeRateShort.structOrderBook.bids![0][0];
                double longAsk = exchangeRateLong.SpotTicker!.structOrderBook.asks![0][0];
                var spread = CalculateSpreadFor(shortBid, longAsk);
                tradeOpportunity.ExchangeLong = exchangeRateLong;
                tradeOpportunity.ExchangeLong.structOrderBook = exchangeRateLong.SpotTicker!.structOrderBook;
                tradeOpportunity.ExchangeShort = exchangeRateShort;
                tradeOpportunity.ExchangeRateA = exchangeRateLong.Exchange == tradeOpportunity.ExchangeRateA!.Exchange ? exchangeRateLong : exchangeRateShort;
                tradeOpportunity.ExchangeRateB = exchangeRateLong.Exchange == tradeOpportunity.ExchangeRateB!.Exchange ? exchangeRateLong : exchangeRateShort;
                tradeOpportunity.Spread = spread;
                tradeOpportunity = (await CalculatePossibleProfit(tradeOpportunity)) ?? tradeOpportunity;
            }
            return tradeOpportunity;
        }
    }
}
