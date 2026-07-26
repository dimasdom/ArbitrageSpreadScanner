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
                    var opportunity = await TryBuildOpportunity(coinData, i, j, maxSpread);
                    if (opportunity is not null)
                        possiblePositions.Add(opportunity);
                }
                coinData.ExchangeRates.RemoveAt(i);
            }
            return possiblePositions;
        }

        private static bool HasUsableSpotTickers(CoinDataModel coinData, int i, int j)
        {
            return coinData.ExchangeRates[i].SpotTicker is not null && coinData.ExchangeRates[j].SpotTicker is not null
                && coinData.ExchangeRates[i].SpotTicker!.structOrderBook.asks?.Count > 0
                && coinData.ExchangeRates[j].SpotTicker!.structOrderBook.asks?.Count > 0;
        }

        private (double spread, bool exchangeAIsFutures) CalculateBestSpread(CoinDataModel coinData, int i, int j)
        {
            double spotAskI = coinData.ExchangeRates[i].SpotTicker!.structOrderBook.asks![0][0];
            double spotAskJ = coinData.ExchangeRates[j].SpotTicker!.structOrderBook.asks![0][0];

            if (coinData.ExchangeRates[i].ExchangeRate > spotAskJ)
                return (CalculateSpreadFor(coinData.ExchangeRates[i].ExchangeRate, spotAskJ), true);

            if (coinData.ExchangeRates[j].ExchangeRate > spotAskI)
                return (CalculateSpreadFor(coinData.ExchangeRates[j].ExchangeRate, spotAskI), false);

            return (0, false);
        }

        private async Task<TradeOpportunityModel?> TryBuildOpportunity(CoinDataModel coinData, int i, int j, double maxSpread)
        {
            if (!HasUsableSpotTickers(coinData, i, j))
                return null;

            var (spread, exchangeAIsFutures) = CalculateBestSpread(coinData, i, j);
            if (spread <= maxSpread || spread >= 50)
                return null;

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
            return await CalculatePossibleProfit(tradeOpportunity);
        }

        public Task<TradeOpportunityModel?> CalculatePossibleProfit(TradeOpportunityModel tradeOpportunity)
        {
            if (tradeOpportunity is null)
                return Task.FromResult<TradeOpportunityModel?>(tradeOpportunity);

            var longSymbol = tradeOpportunity.ExchangeLong!.Symbol;
            ApplySlippageIfOrderBookAvailable(tradeOpportunity.ExchangeLong!, longSymbol);
            ApplySlippageIfOrderBookAvailable(tradeOpportunity.ExchangeShort!, longSymbol);

            var summarySlippage = tradeOpportunity.ExchangeLong!.SummarySlipage + tradeOpportunity.ExchangeShort!.SummarySlipage;
            var summaryTariff = CalculateSummaryTariff(tradeOpportunity);

            tradeOpportunity.SummaryTarrif = summaryTariff;
            tradeOpportunity.PossibleProfit = Math.Abs(tradeOpportunity.Spread) - summaryTariff - summarySlippage;

            return Task.FromResult<TradeOpportunityModel?>(tradeOpportunity);
        }

        private void ApplySlippageIfOrderBookAvailable(ExchangeRateModel exchange, string longSymbol)
        {
            if (!(exchange.structOrderBook.asks?.Count > 0) || !(exchange.structOrderBook.bids?.Count > 0))
                return;

            exchange.SlippageLong = CalculateSlippage(exchange.structOrderBook, longSymbol, _config.PositionSize, true);
            exchange.SlippageShort = CalculateSlippage(exchange.structOrderBook, longSymbol, _config.PositionSize, false);
            exchange.SummarySlipage = exchange.SlippageLong + exchange.SlippageShort;
        }

        private double CalculateSummaryTariff(TradeOpportunityModel tradeOpportunity)
        {
            var longEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeLong!.Exchange];
            var shortEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeShort!.Exchange];
            var longMaker = longEchange.spotMarkets.TryGetValue(tradeOpportunity.ExchangeLong!.Symbol, out var longSpotMarket) && longSpotMarket.maker.HasValue ? longSpotMarket.maker.Value : 0;
            var longExchangeMaker = longMaker > 0.001 ? longMaker * 10 : longMaker * 100;
            var shortSymbolKey = tradeOpportunity.ExchangeShort!.Symbol.Contains(":USDT") ? tradeOpportunity.ExchangeShort!.Symbol : tradeOpportunity.ExchangeShort!.Symbol + ":USDT";
            var shortMaker = shortEchange.markets.TryGetValue(shortSymbolKey, out var shortMarket) && shortMarket.maker.HasValue ? shortMarket.maker.Value : 0;
            var shortExchangeMaker = shortMaker > 0.001 ? shortMaker * 10 : shortMaker * 100;
            return longExchangeMaker * 2 + shortExchangeMaker * 2;
        }
        public static double CalculateSlippage(ccxt.OrderBook orderBook, string symbol, double orderSize, bool isLong)
        {
            var orderBookEntries = isLong ? orderBook.asks : orderBook.bids;
            if (orderBookEntries == null || orderBookEntries.Count == 0)
                throw new InvalidOperationException("Order book is empty!");
            double bestPrice = orderBookEntries[0][0];
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
                throw new InvalidOperationException("Not Enough Liqudidy!");

            double avgFillPrice = totalCost / filledAmount;
            double slippage = (avgFillPrice - bestPrice) / bestPrice * 100;
            return slippage;
        }

        public async Task<TradeOpportunityModel?> WatchPossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            var exchangeRateLong = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeLong!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeLong!.Symbol, false, 30);
            var exchangeRateShort = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeShort!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeShort!.Symbol, false, 30);
            if (exchangeRateLong is null || exchangeRateShort is null
                || exchangeRateShort.structOrderBook.bids?.Count == 0
                || exchangeRateLong.SpotTicker?.structOrderBook.asks?.Count is null or 0)
                return null;
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
            return tradeOpportunity;
        }
    }
}
