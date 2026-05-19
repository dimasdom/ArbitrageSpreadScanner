using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using ccxt.pro;

namespace ArbitrageScanner.Funding.Services
{
    public class FundingPositionCalculatorService
    {
        private readonly ConfigModel _config;
        private readonly DataService _dataService;

        public FundingPositionCalculatorService(IConfiguration configuration, DataService dataService)
        {
            _config = configuration.GetArbitrageConfig();
            _dataService = dataService;
        }

        public double CalculateFundingFor(double fundingRateA, double fundingRateB, out bool isALong)
        {
            // Вычисляем разницу фандингов
            double diff1 = fundingRateA - fundingRateB; // A шорт, B лонг
            double diff2 = fundingRateB - fundingRateA; // B шорт, A лонг

            // Выбираем более выгодную комбинацию
            if (diff1 > diff2)
            {
                isALong = false; // A — long, B — short
                return diff1*100;
            }
            else
            {
                isALong = true; // B — long, A — short
                return diff2*100;
            }
        }

        public async Task<List<TradeOpportunityModel>> FindPossiblePositionsForExchangeSet(CoinDataModel coinData)
        {
            double maxFunding = _config.FundingThresholdRatio;
            List<TradeOpportunityModel> possiblePositions = new List<TradeOpportunityModel>();
            for (var i = coinData.ExchangeRates.Count - 1; i > 0; i--)
            {
                for (var j = i - 1; j >= 0; j--)
                {
                    var fundingRateI = coinData.ExchangeRates[i].FundingRate;
                    var fundingRateJ = coinData.ExchangeRates[j].FundingRate;
                    if (fundingRateI.HasValue && fundingRateJ.HasValue
                        && fundingRateI.Value.fundingRate.HasValue && fundingRateJ.Value.fundingRate.HasValue)
                    {
                        bool isALong;
                        var funding = CalculateFundingFor(
                            fundingRateI.Value.fundingRate.Value,
                            fundingRateJ.Value.fundingRate.Value,
                            out isALong);

                        if (funding > maxFunding)
                        {
                            TradeOpportunityModel tradeOpportunity = new TradeOpportunityModel();
                            tradeOpportunity.ExchangeRateA = coinData.ExchangeRates[i];
                            tradeOpportunity.ExchangeRateB = coinData.ExchangeRates[j];
                            tradeOpportunity.ExchangeLong = isALong ? tradeOpportunity.ExchangeRateA : tradeOpportunity.ExchangeRateB;
                            tradeOpportunity.ExchangeShort = isALong ? tradeOpportunity.ExchangeRateB : tradeOpportunity.ExchangeRateA;
                            tradeOpportunity.TotalFunding = funding;
                            tradeOpportunity.Spread = funding;
                            tradeOpportunity.Symbol = tradeOpportunity.ExchangeRateA.Symbol;
                            possiblePositions.Add((await CalculatePossibleProfit(tradeOpportunity))!);
                        }
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
                var longEchange = _dataService.ExchangeServices[tradeOpportunity.ExchangeLong!.Exchange];
                var shortEchange = _dataService.ExchangeServices[tradeOpportunity.ExchangeShort!.Exchange];
                var longMaker = longEchange.markets.TryGetValue(tradeOpportunity.ExchangeLong!.Symbol, out var longMarket) && longMarket.maker.HasValue ? longMarket.maker.Value : 0;
                var longExchangeMaker = longMaker > 0.001 ? longMaker * 10 : longMaker * 100;
                var shortMaker = shortEchange.markets.TryGetValue(tradeOpportunity.ExchangeShort!.Symbol, out var shortMarket) && shortMarket.maker.HasValue ? shortMarket.maker.Value : 0;
                var shortExchangeMaker = shortMaker > 0.001 ? shortMaker * 10 : shortMaker * 100;
                tradeOpportunity.SummaryTarrif = longExchangeMaker * 2 + shortExchangeMaker * 2;
                tradeOpportunity.PossibleProfit = Math.Abs(tradeOpportunity.TotalFunding) - tradeOpportunity.SummaryTarrif - summarySlippage;
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
    }
}
