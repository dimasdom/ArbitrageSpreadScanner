using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using ProtoBuf;

namespace ArbitrageScanner.Domain.Models
{
    [ProtoContract]
    public enum SpreadType
    {
        [ProtoEnum]
        Futures,
        [ProtoEnum]
        Funding,
        [ProtoEnum]
        Spot
    }
    [ProtoContract]
    public enum OrderStatus
    {
        [ProtoEnum]
        Open,
        [ProtoEnum]
        Update,
        [ProtoEnum]
        Close
    }
    [ProtoContract]
    public class OrderLevelModel
    {
        [ProtoMember(1)]
        public double Price { get; set; }

        [ProtoMember(2)]
        public double Amount { get; set; }
    }
    [ProtoContract]
    public class TradeOpportunityModel
    {
        [BsonRepresentation(BsonType.String)]
        [ProtoMember(1)]
        public Guid Guid { get; set; }

        [ProtoMember(2)]
        public ExchangeRateModel? ExchangeRateA { get; set; }

        [ProtoMember(3)]
        public ExchangeRateModel? ExchangeRateB { get; set; }

        [ProtoMember(4)]
        public ExchangeRateModel? ExchangeShort { get; set; }

        [ProtoMember(5)]
        public ExchangeRateModel? ExchangeLong { get; set; }

        [ProtoMember(6)]
        public double Volatility { get; set; }

        [ProtoMember(7)]
        public double SummaryTarrif { get; set; }

        [ProtoMember(8)]
        public double PossibleProfit { get; set; }

        [ProtoMember(9)]
        public double TotalFunding { get; set; }

        [ProtoMember(10)]
        public double Spread { get; set; }

        [ProtoMember(11)]
        public SpreadType Type { get; set; }
        [ProtoMember(12)]
        public OrderStatus ActionType { get; set; }
        [ProtoMember(13)]
        public double StartSpread {  get; set; }
        [ProtoMember(14)]
        public string? Symbol { get; set; }

        [ProtoMember(15)]
        public List<OrderLevelModel>? BidsExchangeA;
        [ProtoMember(16)]

        public List<OrderLevelModel>? AsksExchangeA;
        [ProtoMember(17)]
        public List<OrderLevelModel>? BidsExchangeB;
        [ProtoMember(18)]

        public List<OrderLevelModel>? AsksExchangeB;
        [ProtoMember(19)]
        public DateTime DateTime { get; set; }

        public void FormatOrdersToSend()
        {
            if(ExchangeRateA?.structOrderBook.asks != null && ExchangeRateA.structOrderBook.bids != null)
            {
                AsksExchangeA = ExchangeRateA.structOrderBook.asks
                    .Select(x => new OrderLevelModel { Price = x[0], Amount = x[1] })
                    .ToList();
                BidsExchangeA = ExchangeRateA.structOrderBook.bids
                    .Select(x => new OrderLevelModel { Price = x[0], Amount = x[1] })
                    .ToList();
            }
            if (ExchangeRateB?.structOrderBook.asks != null && ExchangeRateB.structOrderBook.bids != null)
            {
                AsksExchangeB = ExchangeRateB.structOrderBook.asks
                    .Select(x => new OrderLevelModel { Price = x[0], Amount = x[1] })
                    .ToList();
                BidsExchangeB = ExchangeRateB.structOrderBook.bids
                    .Select(x => new OrderLevelModel { Price = x[0], Amount = x[1] })
                    .ToList();
            }
        }
    }
}
