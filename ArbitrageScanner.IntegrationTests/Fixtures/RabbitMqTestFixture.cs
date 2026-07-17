using ArbitrageScanner.IntegrationTests.Support;
using Testcontainers.RabbitMq;

namespace ArbitrageScanner.IntegrationTests.Fixtures;

public sealed class RabbitMqTestFixture : IAsyncLifetime
{
    public const string BrokerUsername = "integration-test";
    public const string BrokerPassword = "integration-test";

    public const string Exchange = "spread_fanout_exchange";
    public const string QueueApi = "spread_api";
    public const string QueueTelegram = "spread_telegram";

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder(Images.RabbitMq)
        .WithUsername(BrokerUsername)
        .WithPassword(BrokerPassword)
        .Build();

    public (string Host, int Port, string Username, string Password) BrokerEndpoint { get; private set; }

    public async Task InitializeAsync()
    {
        await _rabbitMq.StartAsync();
        BrokerEndpoint = (_rabbitMq.Hostname, _rabbitMq.GetMappedPublicPort(5672), BrokerUsername, BrokerPassword);
    }

    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();
}
