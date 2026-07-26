using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.IntegrationTests.Support;

internal sealed class NoOpTradeOpportunityRepository : ITradeOpportunityRepository
{
    public Task SaveFoundSpread(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task SaveSpreadsTicker(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task SaveSpotSpreadsTicker(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task SaveFoundFundingSpread(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task SaveFoundSpotSpread(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task<List<ProxyModel>> LoadProxies() => Task.FromResult(new List<ProxyModel>());
    public Task SaveError(Exception ex, string? symbol = null, string? method = null, string? exchange = null) => Task.CompletedTask;
    public Task<IEnumerable<TradeOpportunityTickerModel>> GetActivePossiblePositions() => Task.FromResult<IEnumerable<TradeOpportunityTickerModel>>(new List<TradeOpportunityTickerModel>());
    public Task AddActivePossiblePosition(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task UpdateActivePossiblePosition(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
    public Task DeleteActivePossiblePosition(TradeOpportunityModel tradeOpportunity) => Task.CompletedTask;
}
