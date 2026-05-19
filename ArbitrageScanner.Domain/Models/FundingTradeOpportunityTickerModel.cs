using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ArbitrageScanner.Domain.Models
{
    public class FundingTradeOpportunityTickerModel
    {
        public FundingTradeOpportunityTickerModel(TradeOpportunityModel tradeOpportunity)
        {
            Guid = tradeOpportunity.Guid;
            Symbol = tradeOpportunity.ExchangeRateA!.Symbol;
            EchangeA = tradeOpportunity.ExchangeRateA!.Exchange;
            ExchangeB = tradeOpportunity.ExchangeRateB!.Exchange;
            ExchangeLong = tradeOpportunity.ExchangeLong!.Exchange;
            ExchangeShort = tradeOpportunity.ExchangeShort!.Exchange;
            RateA = tradeOpportunity.ExchangeRateA!.ExchangeRate;
            RateB = tradeOpportunity.ExchangeRateB!.ExchangeRate;
            VolumeAskA = tradeOpportunity.ExchangeRateA!.VolumeAsk;
            VolumeBidA = tradeOpportunity.ExchangeRateA!.VolumeBid;
            VolumeAskB = tradeOpportunity.ExchangeRateB!.VolumeAsk;
            VolumeBidB = tradeOpportunity.ExchangeRateB!.VolumeBid;
            SlippageALong = tradeOpportunity.ExchangeRateA!.SlippageLong;
            SlippageAShort = tradeOpportunity.ExchangeRateA!.SlippageShort;
            SlippageBLong = tradeOpportunity.ExchangeRateB!.SlippageLong;
            SlippageBShort = tradeOpportunity.ExchangeRateB!.SlippageShort;
            PossibleProfit = tradeOpportunity.PossibleProfit;
            FundingA = tradeOpportunity.ExchangeRateA!.FundingRate.HasValue && tradeOpportunity.ExchangeRateA.FundingRate.Value.fundingRate.HasValue ? tradeOpportunity.ExchangeRateA.FundingRate.Value.fundingRate.Value : 0;
            FundingB = tradeOpportunity.ExchangeRateB!.FundingRate.HasValue && tradeOpportunity.ExchangeRateB.FundingRate.Value.fundingRate.HasValue ? tradeOpportunity.ExchangeRateB.FundingRate.Value.fundingRate.Value : 0;
            DateTime = DateTime.Now;
        }
        [BsonRepresentation(BsonType.String)]
        public Guid Guid { get; set; }
        public string Symbol { get; set; }
        public string EchangeA { get; set; }
        public string ExchangeB { get; set; }
        public string ExchangeLong { get; set; }
        public string ExchangeShort { get; set; }
        public double RateA { get; set; }
        public double RateB { get; set; }
        public double VolumeAskA { get; set; }
        public double VolumeAskB { get; set; }
        public double VolumeBidA { get; set; }
        public double VolumeBidB { get; set; }
        public double SlippageALong { get; set; }
        public double SlippageBLong { get; set; }
        public double SlippageAShort { get; set; }
        public double SlippageBShort { get; set; }
        public double FundingA { get; set; }
        public double FundingB { get; set; }
        public double PossibleProfit { get; set; }
        public DateTime DateTime { get; set; }
    }
}
