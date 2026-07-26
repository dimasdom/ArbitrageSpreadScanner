using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.IntegrationTests.Fixtures;
using ArbitrageScanner.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ProtoBuf;
using RabbitMQ.Client;
using ArbitrageScanner.Domain.Models;

namespace ArbitrageScanner.IntegrationTests.RabbitMq;

[Collection(RabbitMqCollection.Name)]
public class ServicesCommunicationServiceTests(RabbitMqTestFixture fixture)
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private void SetRabbitMqEnvVars()
    {
        var endpoint = fixture.BrokerEndpoint;
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
    public async Task PostPossiblePosition_PublishesToFanoutQueuesConsumableByRealClients()
    {
        SetRabbitMqEnvVars();
        try
        {
            var dataService = new DataService(new NoOpTradeOpportunityRepository(), EmptyConfig());
            var service = new ServicesCommunicationService(dataService);
            var message = TradeOpportunityModelBuilder.Build();

            await service.PostPossiblePosition(message);

            var received = await ConsumeOneAsync(RabbitMqTestFixture.QueueApi);
            received.Should().NotBeNull();
            received!.Guid.Should().Be(message.Guid);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public async Task PostPossiblePosition_CalledTwice_ReusesConnectionAndChannel()
    {
        SetRabbitMqEnvVars();
        try
        {
            var dataService = new DataService(new NoOpTradeOpportunityRepository(), EmptyConfig());
            var service = new ServicesCommunicationService(dataService);

            await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());
            await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

            var first = await ConsumeOneAsync(RabbitMqTestFixture.QueueTelegram);
            var second = await ConsumeOneAsync(RabbitMqTestFixture.QueueTelegram);
            first.Should().NotBeNull();
            second.Should().NotBeNull();
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    private async Task<TradeOpportunityModel?> ConsumeOneAsync(string queue)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.BrokerEndpoint.Host,
            Port = fixture.BrokerEndpoint.Port,
            UserName = fixture.BrokerEndpoint.Username,
            Password = fixture.BrokerEndpoint.Password
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
            {
                using var stream = new MemoryStream(result.Body.ToArray());
                return Serializer.Deserialize<TradeOpportunityModel>(stream);
            }

            await Task.Delay(200);
        }

        return null;
    }
}
