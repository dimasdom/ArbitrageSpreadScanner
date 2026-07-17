namespace ArbitrageScanner.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoTestFixture>
{
    public const string Name = "Mongo integration tests";
}

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqTestFixture>
{
    public const string Name = "RabbitMq integration tests";
}
