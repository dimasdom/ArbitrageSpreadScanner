using ArbitrageScanner.Domain.Models;
using System.Collections.Concurrent;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class StrategyWatchListService
    {
        private readonly ConcurrentDictionary<string, TradeOpportunityModel> _items = new();

        public ConcurrentDictionary<string, TradeOpportunityModel> Items => _items;

        public bool ContainsKey(string key)
        {
            return _items.ContainsKey(key);
        }

        public bool TryAdd(string key, TradeOpportunityModel value)
        {
            return _items.TryAdd(key, value);
        }

        public bool TryRemove(string key, out TradeOpportunityModel? value)
        {
            return _items.TryRemove(key, out value);
        }

        public void Set(string key, TradeOpportunityModel value)
        {
            _items[key] = value;
        }
    }
}
