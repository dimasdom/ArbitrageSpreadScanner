using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.Domain.Interfaces
{
    public interface ITradeOpportunityRepository
    {
        public Task SaveFoundSpread(TradeOpportunityModel tradeOpportunity);
        public Task SaveSpreadsTicker(TradeOpportunityModel tradeOpportunity);
        public Task SaveSpotSpreadsTicker(TradeOpportunityModel tradeOpportunity);
        public Task SaveFoundFundingSpread(TradeOpportunityModel tradeOpportunity);
        public Task SaveFoundSpotSpread(TradeOpportunityModel tradeOpportunity);

        public Task<List<ProxyModel>> LoadProxies();
        public Task SaveError(Exception ex, string? symbol = null, string? method = null, string? exchange = null);
        public Task<IEnumerable<TradeOpportunityTickerModel>> GetActivePossiblePositions();
        public Task AddActivePossiblePosition(TradeOpportunityModel tradeOpportunity);
        public Task UpdateActivePossiblePosition(TradeOpportunityModel tradeOpportunity);
        public Task DeleteActivePossiblePosition(TradeOpportunityModel tradeOpportunity);
    }
}
