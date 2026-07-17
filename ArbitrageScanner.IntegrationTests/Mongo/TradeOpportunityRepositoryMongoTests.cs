using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.IntegrationTests.Fixtures;
using ArbitrageScanner.IntegrationTests.Support;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ArbitrageScanner.IntegrationTests.Mongo;

[Collection(MongoCollection.Name)]
public class TradeOpportunityRepositoryMongoTests(MongoTestFixture fixture)
{
    [Fact]
    public async Task SaveFoundSpread_PersistsDocumentToFoundSpreadsCollection()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Futures, spread: 3.5);

        await repository.SaveFoundSpread(model);

        var collection = fixture.Database.GetCollection<TradeOpportunityTickerModel>("FoundSpreads");
        var stored = await collection.Find(x => x.Guid == model.Guid).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
        stored!.Symbol.Should().Be(model.Symbol);
        stored.Spread.Should().Be(model.Spread);
        stored.ExchangeA.Should().Be(model.ExchangeRateA!.Exchange);
        stored.ExchangeB.Should().Be(model.ExchangeRateB!.Exchange);
        stored.Type.Should().Be(SpreadType.Futures);
    }

    [Fact]
    public async Task SaveSpreadsTicker_PersistsDocumentToSpreadsTickerCollection()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Futures);

        await repository.SaveSpreadsTicker(model);

        var collection = fixture.Database.GetCollection<TradeOpportunityTickerModel>("SpreadsTicker");
        var stored = await collection.Find(x => x.Guid == model.Guid).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
        stored!.Symbol.Should().Be(model.Symbol);
    }

    [Fact]
    public async Task SaveSpotSpreadsTicker_PersistsDocumentToSpotSpreadsTickerCollection()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Spot);

        await repository.SaveSpotSpreadsTicker(model);

        var collection = fixture.Database.GetCollection<TradeOpportunityTickerModel>("SpotSpreadsTicker");
        var stored = await collection.Find(x => x.Guid == model.Guid).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
        stored!.Type.Should().Be(SpreadType.Spot);
    }

    [Fact]
    public async Task SaveFoundSpotSpread_PersistsDocumentToFoundSpotsCollection()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Spot);

        await repository.SaveFoundSpotSpread(model);

        var collection = fixture.Database.GetCollection<TradeOpportunityTickerModel>("FoundSpots");
        var stored = await collection.Find(x => x.Guid == model.Guid).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveFoundFundingSpread_PersistsDocumentToFoundFundingsCollection()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Funding);

        await repository.SaveFoundFundingSpread(model);

        var collection = fixture.Database.GetCollection<BsonDocument>("FoundFundings");
        var stored = await collection.Find(Builders<BsonDocument>.Filter.Eq("Guid", model.Guid.ToString())).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
        stored!["Symbol"].AsString.Should().Be(model.Symbol);
        stored["EchangeA"].AsString.Should().Be(model.ExchangeRateA!.Exchange);
    }

    [Fact]
    public async Task SaveError_PersistsDocumentToErrorsCollection()
    {
        var repository = fixture.CreateRepository();
        var symbol = $"ERR-{Guid.NewGuid():N}";
        var exception = new InvalidOperationException("boom");

        await repository.SaveError(exception, symbol, "TestMethod", "Binance");

        var collection = fixture.Database.GetCollection<BsonDocument>("Errors");
        var stored = await collection.Find(Builders<BsonDocument>.Filter.Eq("Symbol", symbol)).FirstOrDefaultAsync();

        stored.Should().NotBeNull();
        stored!["Message"].AsString.Should().Be("boom");
        stored["Method"].AsString.Should().Be("TestMethod");
        stored["Exchange"].AsString.Should().Be("Binance");
    }

    [Fact]
    public async Task LoadProxies_ReturnsProxiesSeededDirectlyInMongo()
    {
        var repository = fixture.CreateRepository();
        var marker = Guid.NewGuid().ToString("N");
        var seeded = new ProxyModel { ip = $"10.0.0.1-{marker}", port = 8080, country_code = "US" };

        var proxiesCollection = fixture.Database.GetCollection<ProxyModel>("Proxies");
        await proxiesCollection.InsertOneAsync(seeded);

        var proxies = await repository.LoadProxies();

        proxies.Should().ContainSingle(p => p.ip == seeded.ip && p.port == seeded.port);
    }

    [Fact]
    public async Task ActivePossiblePosition_AddThenGetReturnsIt()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(spread: 1.1);

        await repository.AddActivePossiblePosition(model);
        var positions = await repository.GetActivePossiblePositions();

        positions.Should().ContainSingle(p => p.Guid == model.Guid && p.Spread == model.Spread);
    }

    [Fact]
    public async Task ActivePossiblePosition_UpdateThenGetReflectsChanges()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build(spread: 1.0);
        await repository.AddActivePossiblePosition(model);

        model.Spread = 9.9;
        model.PossibleProfit = 42;
        await repository.UpdateActivePossiblePosition(model);

        var positions = await repository.GetActivePossiblePositions();
        var updated = positions.Should().ContainSingle(p => p.Guid == model.Guid).Which;
        updated.Spread.Should().Be(9.9);
        updated.PossibleProfit.Should().Be(42);
    }

    [Fact]
    public async Task ActivePossiblePosition_DeleteThenGetNoLongerReturnsIt()
    {
        var repository = fixture.CreateRepository();
        var model = TradeOpportunityModelBuilder.Build();
        await repository.AddActivePossiblePosition(model);

        await repository.DeleteActivePossiblePosition(model);

        var positions = await repository.GetActivePossiblePositions();
        positions.Should().NotContain(p => p.Guid == model.Guid);
    }
}
