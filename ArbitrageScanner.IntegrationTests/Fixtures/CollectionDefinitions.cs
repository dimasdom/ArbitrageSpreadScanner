namespace ArbitrageScanner.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoTestFixture>
{
    public const string Name = "Mongo integration tests";
}

