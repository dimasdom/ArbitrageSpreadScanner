using ArbitrageScanner.Infrastructure.HealthChecks;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using ArbitrageScanner.IntegrationTests.Fixtures;
using ArbitrageScanner.IntegrationTests.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ArbitrageScanner.IntegrationTests.HealthChecks;

[Collection(MongoCollection.Name)]
public class MongoHealthCheckTests(MongoTestFixture fixture)
{
    private static IConfiguration BuildConfig(string? connectionString) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arbitrage:MongoDb:ConnectionString"] = connectionString,
        })
        .Build();

    [Fact]
    public async Task CheckHealthAsync_ReachableMongo_ReturnsHealthy()
    {
        var check = new MongoHealthCheck(BuildConfig(fixture.ConnectionString));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_MissingConnectionString_ReturnsUnhealthy()
    {
        var check = new MongoHealthCheck(BuildConfig(null));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}

public class KafkaHealthCheckTests(KafkaProducerTestFixture fixture) : IClassFixture<KafkaProducerTestFixture>
{
    private static KafkaHealthCheck Check(string bootstrapServers) =>
        new(Options.Create(new KafkaOptions { BootstrapServers = bootstrapServers }));

    [Fact]
    public async Task CheckHealthAsync_ReachableKafka_ReturnsHealthy()
    {
        var result = await Check(fixture.BootstrapServers).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_UnreachableKafka_ReturnsUnhealthy()
    {
        var result = await Check("127.0.0.1:1").CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
