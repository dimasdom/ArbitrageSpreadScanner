using ArbitrageScanner.Domain.Models;
using ccxt;
using System.Collections.Concurrent;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ExchangeRegistry
    {
        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> ExchangeMarkets { get; } = new();
        public ConcurrentDictionary<string, Dictionary<string, MarketInterface>> ExchangeSpotMarkets { get; } = new();
        public ConcurrentDictionary<string, Exchange> Exchanges { get; } = new();
        public ConcurrentDictionary<string, ExchangeService> ExchangeServices { get; } = new();
        public ConcurrentDictionary<string, ExchangeService> ExchangeObserverServices { get; } = new();
        public List<string> ExchangeInitList { get; }

        public ExchangeRegistry(List<string> exchangeInitList)
        {
            ExchangeInitList = exchangeInitList;
        }
    }
}
