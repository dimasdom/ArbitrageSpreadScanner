using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbitrageScanner.Domain.Models
{
    public class ConfigModel
    {
        public double SpreadSize { get; set; }
        public double PositionSize { get; set; }
        public double KeepWatchingSpread { get; set; }
        public int ThreadCount { get; set; }
        public double FundingThresholdRatio { get; set; }
        public string? TelegramToken { get; set; }
        public string? ChatId { get; set; }
        public bool Futures {  get; set; }
        public bool Funding {  get; set; }
        public bool Spot {  get; set; }
        public List<string> ExchangeList { get; set; } = new();
        public List<ProxyModel> ProxyList { get; set; } = new();
        public MongoDbConfig MongoDb { get; set; } = new();
    }
}
