using System.Diagnostics;
using System.Diagnostics.Metrics;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.IntegrationTests.Fixtures;
using ArbitrageScanner.IntegrationTests.Support;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ArbitrageScanner.IntegrationTests.Kafka;

/// <summary>Dual publish during the RabbitMQ -> Kafka migration. Shares the RabbitMQ collection because the publisher reads
/// its connection settings from process-wide environment variables.</summary>
[Collection(RabbitMqCollection.Name)]
public sealed class DualPublishTests(RabbitMqTestFixture rabbit, KafkaProducerTestFixture kafka) : IClassFixture<KafkaProducerTestFixture>
{
    private static DataService NewDataService() =>
        new(new NoOpTradeOpportunityRepository(), new ConfigurationBuilder().Build(), NullLogger<DataService>.Instance);

    private void UseRabbitMq()
    {
        var endpoint = rabbit.BrokerEndpoint;
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", endpoint.Host);
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", endpoint.Port.ToString());
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", endpoint.Username);
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", endpoint.Password);
    }

    private static void ForgetRabbitMq()
    {
        foreach (var name in new[] { "RABBITMQ_HOST", "RABBITMQ_PORT", "RABBITMQ_USERNAME", "RABBITMQ_PASSWORD" })
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private (ServicesCommunicationService Service, KafkaMessageProducer Producer) CreateService(string topic, bool rabbitMq, bool kafkaOn, string? bootstrap = null)
    {
        var kafkaOptions = new KafkaOptions { BootstrapServers = bootstrap ?? kafka.BootstrapServers, SpreadsTopic = topic, LingerMs = 0, DeliveryTimeoutMs = 2_000 };
        var producer = new KafkaMessageProducer(Options.Create(kafkaOptions), NullLogger<KafkaMessageProducer>.Instance);
        var service = new ServicesCommunicationService(
            NewDataService(),
            Options.Create(new MessagingPublishOptions { RabbitMq = rabbitMq, Kafka = kafkaOn }),
            producer,
            Options.Create(kafkaOptions),
            NullLogger<ServicesCommunicationService>.Instance);
        return (service, producer);
    }

    /// <summary>The queue is shared with other tests, so skip anything that is not the message under test.</summary>
    private async Task<(IReadOnlyBasicProperties Properties, byte[] Body)?> ReceiveFromRabbitMqAsync(string queue, Func<byte[], bool>? match = null)
    {
        var factory = new ConnectionFactory { HostName = rabbit.BrokerEndpoint.Host, Port = rabbit.BrokerEndpoint.Port, UserName = rabbit.BrokerEndpoint.Username, Password = rabbit.BrokerEndpoint.Password };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is null)
            {
                await Task.Delay(200);
                continue;
            }

            var body = result.Body.ToArray();
            if (match is null || match(body))
            {
                return (result.BasicProperties, body);
            }
        }

        return null;
    }

    private static bool Contains(byte[] body, Guid guid)
    {
        using var stream = new MemoryStream(body);
        return ProtoBuf.Serializer.Deserialize<ArbitrageScanner.Domain.Models.TradeOpportunityModel>(stream).Guid == guid;
    }

    [Fact]
    public async Task Both_brokers_receive_the_same_bytes_message_id_and_timestamp()
    {
        UseRabbitMq();
        try
        {
            var topic = await kafka.CreateTopicAsync(partitions: 3);
            var (service, producer) = CreateService(topic, rabbitMq: true, kafkaOn: true);
            using var _ = producer;
            var message = TradeOpportunityModelBuilder.Build(symbol: "SOL/USDT");

            await service.PostPossiblePosition(message);

            var fromKafka = kafka.ReadAll(topic, expected: 1).Should().ContainSingle().Subject;
            var fromRabbit = await ReceiveFromRabbitMqAsync(RabbitMqTestFixture.QueueApi, body => body.AsSpan().SequenceEqual(fromKafka.Message.Value));

            fromRabbit.Should().NotBeNull();
            var messageId = fromRabbit!.Value.Properties.MessageId;
            messageId.Should().NotBeNullOrEmpty();
            MessageHeaders.Read(fromKafka.Message.Headers).MessageId.Should().Be(messageId, "the same event carries one identity on both brokers");
            fromKafka.Message.Value.Should().Equal(fromRabbit.Value.Body, "the payload bytes are identical");
            new DateTimeOffset(fromKafka.Message.Timestamp.UtcDateTime, TimeSpan.Zero).ToUnixTimeSeconds()
                .Should().Be(fromRabbit.Value.Properties.Timestamp.UnixTime, "both brokers carry the same producer timestamp for the parity check");
            fromKafka.Message.Key.Should().Be("futures:binance:bybit:SOL-USDT");
            MessageHeaders.Read(fromKafka.Message.Headers).MessageType.Should().Be("TradeOpportunityModel");
        }
        finally
        {
            ForgetRabbitMq();
        }
    }

    [Fact]
    public async Task Each_event_gets_its_own_message_id()
    {
        UseRabbitMq();
        try
        {
            var topic = await kafka.CreateTopicAsync(partitions: 1);
            var (service, producer) = CreateService(topic, rabbitMq: true, kafkaOn: true);
            using var _ = producer;

            await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());
            await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

            kafka.ReadAll(topic, expected: 2).Select(r => MessageHeaders.Read(r.Message.Headers).MessageId).Distinct().Should().HaveCount(2);
        }
        finally
        {
            ForgetRabbitMq();
        }
    }

    [Fact]
    public async Task A_dead_kafka_broker_neither_fails_nor_slows_the_rabbitmq_publish_in_shadow_mode()
    {
        UseRabbitMq();
        var failures = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaPublishMetrics.MeterName && instrument.Name.EndsWith("failures", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref failures, value));
        listener.Start();
        try
        {
            var (service, producer) = CreateService("spreads", rabbitMq: true, kafkaOn: true, bootstrap: "127.0.0.1:1");
            using var _ = producer;
            var message = TradeOpportunityModelBuilder.Build();

            var stopwatch = Stopwatch.StartNew();
            await service.PostPossiblePosition(message);
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5), "shadow Kafka publishing must not block the observers");
            (await ReceiveFromRabbitMqAsync(RabbitMqTestFixture.QueueApi, body => Contains(body, message.Guid))).Should().NotBeNull("RabbitMQ stays authoritative");

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (Interlocked.Read(ref failures) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            Interlocked.Read(ref failures).Should().BeGreaterThan(0, "the failure is counted so it is visible even though it is swallowed");
        }
        finally
        {
            ForgetRabbitMq();
        }
    }

    [Fact]
    public async Task With_rabbitmq_off_kafka_is_authoritative_and_rabbitmq_is_never_contacted()
    {
        ForgetRabbitMq();
        var topic = await kafka.CreateTopicAsync(partitions: 1);
        var (service, producer) = CreateService(topic, rabbitMq: false, kafkaOn: true);
        using var _ = producer;

        await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

        kafka.ReadAll(topic, expected: 1).Should().ContainSingle();
    }

    [Fact]
    public async Task With_rabbitmq_off_a_kafka_failure_propagates_like_a_rabbitmq_failure_does_today()
    {
        ForgetRabbitMq();
        var (service, producer) = CreateService("spreads", rabbitMq: false, kafkaOn: true, bootstrap: "127.0.0.1:1");
        using var _ = producer;

        var act = () => service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

        await act.Should().ThrowAsync<ProduceException<string, byte[]>>();
    }

    [Fact]
    public async Task Kafka_off_publishes_to_rabbitmq_only_exactly_as_before()
    {
        UseRabbitMq();
        try
        {
            var topic = await kafka.CreateTopicAsync(partitions: 1);
            var (service, producer) = CreateService(topic, rabbitMq: true, kafkaOn: false);
            using var _ = producer;

            var message = TradeOpportunityModelBuilder.Build();
            await service.PostPossiblePosition(message);

            (await ReceiveFromRabbitMqAsync(RabbitMqTestFixture.QueueApi, body => Contains(body, message.Guid))).Should().NotBeNull();
            kafka.ReadAll(topic, expected: 1, timeoutSeconds: 3).Should().BeEmpty();
        }
        finally
        {
            ForgetRabbitMq();
        }
    }
}
