using ArbitrageScanner.Infrastructure.HealthChecks;
using ArbitrageScanner.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

[Collection(RabbitMqCollection.Name)]
public class RabbitMqHealthCheckTests(RabbitMqTestFixture fixture)
{
    private static void SetRabbitMqEnvVars((string Host, int Port, string Username, string Password) endpoint)
    {
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", endpoint.Host);
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", endpoint.Port.ToString());
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", endpoint.Username);
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", endpoint.Password);
    }

    private static void ClearRabbitMqEnvVars()
    {
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", null);
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", null);
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", null);
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", null);
    }

    [Fact]
    public async Task CheckHealthAsync_ReachableRabbitMq_ReturnsHealthy()
    {
        SetRabbitMqEnvVars(fixture.BrokerEndpoint);
        try
        {
            var check = new RabbitMqHealthCheck();

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Healthy);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WrongCredentials_ReturnsUnhealthy()
    {
        var endpoint = fixture.BrokerEndpoint;
        SetRabbitMqEnvVars((endpoint.Host, endpoint.Port, endpoint.Username, "wrong-password"));
        try
        {
            var check = new RabbitMqHealthCheck();

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }
}
