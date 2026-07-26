using ArbitrageScanner.Domain.Models;
using MongoDB.Driver;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class MongoService
    {
        private readonly IMongoDatabase _database;
        private readonly FileService? _fileService;

        public MongoService(string connectionString, string databaseName, FileService? fileService = null)
        {
            _fileService = fileService;
            var resolvedConnectionString = string.IsNullOrWhiteSpace(connectionString)
                ? Environment.GetEnvironmentVariable("MongoDb_ConnectionString")
                : connectionString;

            var resolvedDatabaseName = string.IsNullOrWhiteSpace(databaseName)
                ? Environment.GetEnvironmentVariable("MongoDb_DatabaseName")
                : databaseName;

            if (string.IsNullOrWhiteSpace(resolvedConnectionString))
            {
                throw new InvalidOperationException("MongoDB connection string is not configured. Provide it via constructor or MongoDb_ConnectionString environment variable.");
            }

            if (string.IsNullOrWhiteSpace(resolvedDatabaseName))
            {
                resolvedDatabaseName = "SwapArbitrage";
            }

            var client = new MongoClient(resolvedConnectionString);
            _database = client.GetDatabase(resolvedDatabaseName);
        }

        public async Task InsertToFoundSpreadsAsync(TradeOpportunityTickerModel model)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("FoundSpreads");
            await collection.InsertOneAsync(model);
        }

        public async Task InsertToSpreadsTickerAsync(TradeOpportunityTickerModel model)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("SpreadsTicker");
            await collection.InsertOneAsync(model);
        }

        public async Task InsertToSpotSpreadsTickerAsync(TradeOpportunityTickerModel model)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("SpotSpreadsTicker");
            await collection.InsertOneAsync(model);
        }

        public async Task InsertToFoundFundingAsync(FundingTradeOpportunityTickerModel tradeOpportunity)
        {
            var collection = _database.GetCollection<FundingTradeOpportunityTickerModel>("FoundFundings");
            await collection.InsertOneAsync(tradeOpportunity);
        }
        public async Task InsertToFoundSpotAsync(TradeOpportunityTickerModel tradeOpportunity)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("FoundSpots");
            await collection.InsertOneAsync(tradeOpportunity);
        }


        public async Task LogError(string? message = null, string? symbol = null, string? method = null, string? exchange = null, string? stackTrace = null)
        {
            var collection = _database.GetCollection<LogErrorModel>("Errors");
            var model = new LogErrorModel { Message = message, Symbol = symbol, Method = method, Exchange = exchange, DateTime = DateTime.Now, StackTrace= stackTrace};
            await collection.InsertOneAsync(model);
        }
        public async Task<IEnumerable<ProxyModel>> GetProxies()
        {
            var collection = _database.GetCollection<ProxyModel>("Proxies");
            var allProxies = await collection.Find(Builders<ProxyModel>.Filter.Empty).ToListAsync();
            if(allProxies.Count == 0)
            {
                allProxies = _fileService?.LoadProxyList() ?? new List<ProxyModel>();
                if (allProxies.Count > 0)
                {
                    foreach (var proxy in allProxies)
                        proxy.id = MongoDB.Bson.ObjectId.GenerateNewId();
                    await collection.InsertManyAsync(allProxies);
                    return allProxies;
                }
            }else
            {
                return allProxies;
            }
            return Enumerable.Empty<ProxyModel>();
        }

        public async Task AddActivePossiblePosition(TradeOpportunityTickerModel tradeOpportunity)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("ActivePossiblePositions");
            await collection.InsertOneAsync(tradeOpportunity);
        }
        public async Task UpdatePossiblePosition(TradeOpportunityTickerModel tradeOpportunity)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("ActivePossiblePositions");
            var filter = Builders<TradeOpportunityTickerModel>.Filter.Eq("Guid", tradeOpportunity.Guid.ToString());
            var update = Builders<TradeOpportunityTickerModel>.Update
                .Set(p => p.RateA, tradeOpportunity.RateA)
                .Set(p => p.RateB, tradeOpportunity.RateB)
                .Set(p => p.VolumeAskA, tradeOpportunity.VolumeAskA)
                .Set(p => p.VolumeAskB, tradeOpportunity.VolumeAskB)
                .Set(p => p.VolumeBidA, tradeOpportunity.VolumeBidA)
                .Set(p => p.VolumeBidB, tradeOpportunity.VolumeBidB)
                .Set(p => p.SlippageALong, tradeOpportunity.SlippageALong)
                .Set(p => p.SlippageAShort, tradeOpportunity.SlippageAShort)
                .Set(p => p.SlippageBLong, tradeOpportunity.SlippageBLong)
                .Set(p => p.SlippageBShort, tradeOpportunity.SlippageBShort)
                .Set(p => p.PossibleProfit, tradeOpportunity.PossibleProfit)
                .Set(p => p.FundingA, tradeOpportunity.FundingA)
                .Set(p => p.FundingB, tradeOpportunity.FundingB)
                .Set(p => p.Spread, tradeOpportunity.Spread)
                .Set(p => p.DateTime, DateTime.Now);
            await collection.UpdateOneAsync(filter, update);
        }
        public async Task DeleteActivePossiblePosition(string id)
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("ActivePossiblePositions");
            var filter = Builders<TradeOpportunityTickerModel>.Filter.Eq("Guid", id);
            await collection.DeleteOneAsync(filter);
        }
        public async Task<IEnumerable<TradeOpportunityTickerModel>> GetActivePossiblePositions()
        {
            var collection = _database.GetCollection<TradeOpportunityTickerModel>("ActivePossiblePositions");
            var allPositions = await collection.Find(Builders<TradeOpportunityTickerModel>.Filter.Empty).ToListAsync();
            return allPositions;
        }
    }
}
