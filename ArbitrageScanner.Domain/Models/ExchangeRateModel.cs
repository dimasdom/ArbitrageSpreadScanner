using ccxt;
using ccxt.pro;
using MongoDB.Bson.Serialization.Attributes;
using ProtoBuf;

namespace ArbitrageScanner.Domain.Models
{
    [ProtoContract]
    public class ExchangeRateModel
    {
        [ProtoMember(1)]
        public required string Symbol { get; set; }

        [ProtoMember(2)]
        public required string Exchange { get; set; }

        [ProtoMember(3)]
        public double ExchangeRate { get; set; }

        [ProtoMember(4)]
        public double VolumeAsk { get; set; }

        [ProtoMember(5)]
        public double VolumeBid { get; set; }

        [ProtoMember(6)]
        public double SlippageLong { get; set; }

        [ProtoMember(7)]
        public double SlippageShort { get; set; }

        [ProtoMember(8)]
        public double SummarySlipage { get; set; }

        [ProtoMember(10)]
        public double? FundingRateValue { get; set; }
        public FundingRate? FundingRate { get; set; }
        [BsonIgnore]
        public IOrderBook? OrderBook { get; set; }
        [BsonIgnore]
        public ccxt.OrderBook structOrderBook { get; set; }
        [BsonIgnore]
        public ccxt.Ticker Ticker { get; set; }
        [BsonIgnore]
        public ExchangeRateModel? SpotTicker { get; set; }

    }
}
