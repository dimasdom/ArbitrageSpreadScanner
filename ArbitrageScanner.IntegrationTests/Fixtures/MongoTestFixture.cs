using ArbitrageScanner.Infrastructure.Repositories;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.IntegrationTests.Support;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace ArbitrageScanner.IntegrationTests.Fixtures;

public sealed class MongoTestFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder(Images.Mongo).Build();

    public const string DatabaseName = "ArbitrageScannerIntegrationTests";

    private IMongoDatabase _database = default!;

    public Task InitializeAsync() => _mongo.StartAsync();

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();

    public string ConnectionString => _mongo.GetConnectionString();

    public TradeOpportunityRepositoryMongo CreateRepository() =>
        new(new MongoService(_mongo.GetConnectionString(), DatabaseName));

    public IMongoDatabase Database => _database ??=
        new MongoClient(_mongo.GetConnectionString()).GetDatabase(DatabaseName);
}
