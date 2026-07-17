using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.IntegrationTests.Fixtures;
using ArbitrageScanner.IntegrationTests.Support;
using FluentAssertions;
using ProtoBuf;
using RabbitMQ.Client;

namespace ArbitrageScanner.IntegrationTests.RabbitMq;

[Collection(RabbitMqCollection.Name)]
public class SpreadFanoutContractTests(RabbitMqTestFixture fixture)
{
    [Fact]
    public async Task PublishedSpread_IsFannedOutToBothSpreadApiAndSpreadTelegramQueues()
    {
        var message = TradeOpportunityModelBuilder.Build(type: SpreadType.Futures, actionType: OrderStatus.Open);

        await PublishAsync(message);

        var fromApiQueue = await ConsumeOneAsync(RabbitMqTestFixture.QueueApi);
        var fromTelegramQueue = await ConsumeOneAsync(RabbitMqTestFixture.QueueTelegram);

        fromApiQueue.Should().NotBeNull("the fanout exchange should route to spread_api");
        fromTelegramQueue.Should().NotBeNull("the fanout exchange should route to spread_telegram");

        fromApiQueue!.Guid.Should().Be(message.Guid);
        fromTelegramQueue!.Guid.Should().Be(message.Guid);
    }

    [Fact]
    public async Task PublishedSpread_RoundTripsProtobufFieldsFaithfully()
    {
        var message = TradeOpportunityModelBuilder.Build(
            type: SpreadType.Funding,
            actionType: OrderStatus.Close,
            spread: 4.2,
            possibleProfit: 99);

        await PublishAsync(message);
        var received = await ConsumeOneAsync(RabbitMqTestFixture.QueueApi);

        received.Should().NotBeNull();
        received!.Symbol.Should().Be(message.Symbol);
        received.Type.Should().Be(SpreadType.Funding);
        received.ActionType.Should().Be(OrderStatus.Close);
        received.Spread.Should().Be(message.Spread);
        received.PossibleProfit.Should().Be(message.PossibleProfit);
        received.ExchangeRateA!.Exchange.Should().Be(message.ExchangeRateA!.Exchange);
        received.ExchangeRateB!.Exchange.Should().Be(message.ExchangeRateB!.Exchange);
        received.ExchangeLong!.Exchange.Should().Be(message.ExchangeLong!.Exchange);
        received.ExchangeShort!.Exchange.Should().Be(message.ExchangeShort!.Exchange);
    }

    private async Task PublishAsync(TradeOpportunityModel message)
    {
        message.FormatOrdersToSend();

        await using var connection = await CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(RabbitMqTestFixture.Exchange, ExchangeType.Fanout, durable: true);
        await channel.QueueDeclareAsync(RabbitMqTestFixture.QueueTelegram, durable: false, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(RabbitMqTestFixture.QueueTelegram, RabbitMqTestFixture.Exchange, routingKey: "");
        await channel.QueueDeclareAsync(RabbitMqTestFixture.QueueApi, durable: false, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(RabbitMqTestFixture.QueueApi, RabbitMqTestFixture.Exchange, routingKey: "");

        await channel.QueuePurgeAsync(RabbitMqTestFixture.QueueTelegram);
        await channel.QueuePurgeAsync(RabbitMqTestFixture.QueueApi);

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);

        await channel.BasicPublishAsync(RabbitMqTestFixture.Exchange, routingKey: "", body: stream.ToArray());
    }

    private async Task<TradeOpportunityModel?> ConsumeOneAsync(string queue)
    {
        await using var connection = await CreateConnectionAsync();
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

    private Task<IConnection> CreateConnectionAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.BrokerEndpoint.Host,
            Port = fixture.BrokerEndpoint.Port,
            UserName = fixture.BrokerEndpoint.Username,
            Password = fixture.BrokerEndpoint.Password
        };

        return factory.CreateConnectionAsync();
    }
}
