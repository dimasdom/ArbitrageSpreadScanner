using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;

namespace ArbitrageScanner.Infrastructure.Repositories
{
    public class TradeOpportunityRepositoryMongo : ITradeOpportunityRepository
    {
        private readonly MongoService _mongoService;

        public TradeOpportunityRepositoryMongo()
        {
            var mongoConnectionString = Environment.GetEnvironmentVariable("MongoDb_ConnectionString") ?? string.Empty;
            var mongoDatabaseName = Environment.GetEnvironmentVariable("MongoDb_DatabaseName") ?? "SwapArbitrage";
            _mongoService = new MongoService(mongoConnectionString, mongoDatabaseName, null);
        }

        public TradeOpportunityRepositoryMongo(MongoService mongoService)
        {
            _mongoService = mongoService;
        }

        public TradeOpportunityRepositoryMongo(FileService fileService)
        {
            var mongoConnectionString = Environment.GetEnvironmentVariable("MongoDb_ConnectionString") ?? string.Empty;
            var mongoDatabaseName = Environment.GetEnvironmentVariable("MongoDb_DatabaseName") ?? "SwapArbitrage";
            _mongoService = new MongoService(mongoConnectionString, mongoDatabaseName, fileService);
        }

        public  async Task SaveFoundSpread(TradeOpportunityModel tradeOpportunity)
        {
            var foundSpread = new TradeOpportunityTickerModel(tradeOpportunity);
            await _mongoService.InsertToFoundSpreadsAsync(foundSpread);
        }
        public  async Task SaveSpreadsTicker(TradeOpportunityModel tradeOpportunity)
        {
            var spreadsTikcer = new TradeOpportunityTickerModel(tradeOpportunity);
            await _mongoService.InsertToSpreadsTickerAsync(spreadsTikcer);
        }
        public  async Task SaveSpotSpreadsTicker(TradeOpportunityModel tradeOpportunity)
        {
            var spreadsTikcer = new TradeOpportunityTickerModel(tradeOpportunity);
            await _mongoService.InsertToSpotSpreadsTickerAsync(spreadsTikcer);
        }
        public  async Task SaveFoundFundingSpread(TradeOpportunityModel tradeOpportunity)
        {
            var possibleFundingPosition = new FundingTradeOpportunityTickerModel(tradeOpportunity);
            await _mongoService.InsertToFoundFundingAsync(possibleFundingPosition);
        }
        public  async Task SaveFoundSpotSpread(TradeOpportunityModel tradeOpportunity)
        {
            var possibleSpotPosition = new TradeOpportunityTickerModel(tradeOpportunity);
            await _mongoService.InsertToFoundSpotAsync(possibleSpotPosition);
        }

        public  async Task<List<ProxyModel>> LoadProxies()
        {
            List<ProxyModel> Proxies = new List<ProxyModel>();
            Proxies.AddRange(await _mongoService.GetProxies());
            Proxies.Shuffle();
            return Proxies;
        }
        public async Task SaveError(Exception ex, string? symbol = null!, string? method = null!, string? exchange = null!)
        {
            await _mongoService.LogError(ex.Message, symbol, method, exchange, ex.StackTrace!);
        }

        public Task<IEnumerable<TradeOpportunityTickerModel>> GetActivePossiblePositions()
        {
            return _mongoService.GetActivePossiblePositions();
        }

        public Task AddActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            var ticker = new TradeOpportunityTickerModel(tradeOpportunity);
            return _mongoService.AddActivePossiblePosition(ticker);
        }

        public Task DeleteActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            return _mongoService.DeleteActivePossiblePosition(tradeOpportunity.Guid.ToString());
        }

        public Task UpdateActivePossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            var ticker = new TradeOpportunityTickerModel(tradeOpportunity);
            return _mongoService.UpdatePossiblePosition(ticker);
        }
    }
}
