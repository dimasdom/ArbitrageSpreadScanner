using ArbitrageScanner.IntegrationTests.Support;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

namespace ArbitrageScanner.IntegrationTests.Kafka;

public sealed class KafkaProducerTestFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder(Images.Kafka).Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public Task InitializeAsync() => _kafka.StartAsync();

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    public async Task<string> CreateTopicAsync(int partitions)
    {
        var topic = "spreads-" + Guid.NewGuid().ToString("N")[..12];

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 }]);
        return topic;
    }

    public List<ConsumeResult<string, byte[]>> ReadAll(string topic, int expected, int timeoutSeconds = 30)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = "reader-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var results = new List<ConsumeResult<string, byte[]>>();
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaProducerCollection : ICollectionFixture<KafkaProducerTestFixture>
{
    public const string Name = "Kafka producer integration tests";
}
