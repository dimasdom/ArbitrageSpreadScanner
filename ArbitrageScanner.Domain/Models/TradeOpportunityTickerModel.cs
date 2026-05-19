using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ArbitrageScanner.Domain.Models
{
    public class TradeOpportunityTickerModel
    {
        public TradeOpportunityTickerModel(TradeOpportunityModel tradeOpportunity)
        {
            Guid = tradeOpportunity.Guid;
            Type = tradeOpportunity.Type;
            Symbol = tradeOpportunity.ExchangeRateA!.Symbol;
            Spread = tradeOpportunity.Spread;
            StartSpread = tradeOpportunity.StartSpread;
            ExchangeA = tradeOpportunity.ExchangeRateA!.Exchange;
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
            FundingA = tradeOpportunity.ExchangeRateA!.FundingRate.HasValue && tradeOpportunity.ExchangeRateA.FundingRate.Value.fundingRate.HasValue ? tradeOpportunity.ExchangeRateA.FundingRate.Value.fundingRate.Value : null;
            FundingB = tradeOpportunity.ExchangeRateB!.FundingRate.HasValue && tradeOpportunity.ExchangeRateB.FundingRate.Value.fundingRate.HasValue ? tradeOpportunity.ExchangeRateB.FundingRate.Value.fundingRate.Value : null;
            PossibleProfit = tradeOpportunity.PossibleProfit;
            DateTime = DateTime.Now;
        }
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        [BsonRepresentation(BsonType.String)]
        public Guid Guid { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public SpreadType Type { get; set; }
        public string Symbol { get; set; }
        public double Spread { get; set; }
        public double StartSpread { get; set; }
        public string ExchangeA { get; set; }
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
        public double PossibleProfit { get; set; }
        public double? FundingA { get; set; } = default!;
        public double? FundingB { get; set; } = default!;
        public DateTime DateTime { get; set; }
    }
}
