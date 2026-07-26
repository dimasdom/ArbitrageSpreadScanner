using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using ccxt.pro;

namespace ArbitrageScanner.Futures.Services
{
    public class FuturesPositionCalculatorService
    {
        private readonly ConfigModel _config;
        private readonly DataService _dataService;

        public FuturesPositionCalculatorService(IConfiguration configuration, DataService dataService)
        {
            _config = configuration.GetArbitrageConfig();
            _dataService = dataService;
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
                    var spread = CalculateSpreadFor(coinData.ExchangeRates[i].ExchangeRate, coinData.ExchangeRates[j].ExchangeRate);

                    if (Math.Abs(spread) > maxSpread && Math.Abs(spread) < 50)
                    {
                        TradeOpportunityModel tradeOpportunity = new TradeOpportunityModel();
                        tradeOpportunity.ExchangeRateA = coinData.ExchangeRates[i];
                        tradeOpportunity.ExchangeRateB = coinData.ExchangeRates[j];
                        tradeOpportunity.Spread = spread;
                        tradeOpportunity.ExchangeLong = spread < 0 ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
                        tradeOpportunity.ExchangeShort = spread > 0 ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
                        tradeOpportunity.Symbol = tradeOpportunity.ExchangeRateA.Symbol;
                        possiblePositions.Add((await CalculatePossibleProfit(tradeOpportunity))!);
                    }
                }
                coinData.ExchangeRates.RemoveAt(i);
            }
            return possiblePositions;
        }

        public Task<TradeOpportunityModel?> CalculatePossibleProfit(TradeOpportunityModel tradeOpportunity)
        {
            if (tradeOpportunity is not null)
            {
                tradeOpportunity.ExchangeLong!.SlippageLong = CalculateSlippage(tradeOpportunity.ExchangeLong!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, true);
                tradeOpportunity.ExchangeLong!.SlippageShort = CalculateSlippage(tradeOpportunity.ExchangeLong!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, false);
                tradeOpportunity.ExchangeLong!.SummarySlipage = tradeOpportunity.ExchangeLong!.SlippageLong + tradeOpportunity.ExchangeLong!.SlippageShort;
                tradeOpportunity.ExchangeShort!.SlippageLong = CalculateSlippage(tradeOpportunity.ExchangeShort!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, true);
                tradeOpportunity.ExchangeShort!.SlippageShort = CalculateSlippage(tradeOpportunity.ExchangeShort!.structOrderBook, tradeOpportunity.ExchangeLong!.Symbol, _config.PositionSize, false);
                tradeOpportunity.ExchangeShort!.SummarySlipage = tradeOpportunity.ExchangeShort!.SlippageLong + tradeOpportunity.ExchangeShort!.SlippageShort;
                var summarySlippage = tradeOpportunity.ExchangeLong!.SummarySlipage + tradeOpportunity.ExchangeShort!.SummarySlipage;
                var longEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeLong!.Exchange];
                var shortEchange = _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeShort!.Exchange];
                var longMaker = longEchange.markets.TryGetValue(tradeOpportunity.ExchangeLong!.Symbol, out var longMarket) && longMarket.maker.HasValue ? longMarket.maker.Value : 0;
                var longExchangeMaker = longMaker > 0.001 ? longMaker * 10 : longMaker * 100;
                var shortMaker = shortEchange.markets.TryGetValue(tradeOpportunity.ExchangeShort!.Symbol, out var shortMarket) && shortMarket.maker.HasValue ? shortMarket.maker.Value : 0;
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
            var exchangeRateA = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateA!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeRateA!.Symbol, false, 30, onlyFutures: true);
            var exchangeRateB = await _dataService.ExchangeObserverServices[tradeOpportunity.ExchangeRateB!.Exchange].GetDataForCoin(tradeOpportunity.ExchangeRateA!.Symbol, false, 30, onlyFutures: true);
            if (exchangeRateA is null || exchangeRateB is null)
                return null;
            var spread = CalculateSpreadFor(exchangeRateA.ExchangeRate, exchangeRateB.ExchangeRate);
            tradeOpportunity.ExchangeRateA = exchangeRateA;
            tradeOpportunity.ExchangeRateB = exchangeRateB;
            tradeOpportunity.Spread = spread;
            tradeOpportunity.ExchangeLong = spread < 0 ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
            tradeOpportunity.ExchangeShort = spread > 0 ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
            tradeOpportunity = (await CalculatePossibleProfit(tradeOpportunity)) ?? tradeOpportunity;
            return tradeOpportunity;
        }
    }
}
