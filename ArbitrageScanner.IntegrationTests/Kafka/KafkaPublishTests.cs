using System.Diagnostics.Metrics;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.IntegrationTests.Support;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace ArbitrageScanner.IntegrationTests.Kafka;

/// <summary>The scanner's publisher end to end against a real broker: <see cref="ServicesCommunicationService"/> is what the
/// Futures / Spot / Funding observers call.</summary>
public sealed class KafkaPublishTests(KafkaProducerTestFixture kafka) : IClassFixture<KafkaProducerTestFixture>
{
    private static DataService NewDataService() =>
        new(new NoOpTradeOpportunityRepository(), new ConfigurationBuilder().Build(), NullLogger<DataService>.Instance);

    private static (ServicesCommunicationService Service, KafkaMessageProducer Producer) Create(string bootstrap, string topic)
    {
        var options = Options.Create(new KafkaOptions { BootstrapServers = bootstrap, SpreadsTopic = topic, LingerMs = 0, DeliveryTimeoutMs = 2_000 });
        var producer = new KafkaMessageProducer(options, NullLogger<KafkaMessageProducer>.Instance);
        return (new ServicesCommunicationService(NewDataService(), producer, options), producer);
    }

    [Fact]
    public async Task Publishes_the_event_with_its_spread_key_message_id_and_producer_timestamp()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 3);
        var (service, producer) = Create(kafka.BootstrapServers, topic);
        using var _ = producer;
        var message = TradeOpportunityModelBuilder.Build(symbol: "SOL/USDT");
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        await service.PostPossiblePosition(message);

        var record = kafka.ReadAll(topic, expected: 1).Should().ContainSingle().Subject;
        record.Message.Key.Should().Be("futures:binance:bybit:SOL-USDT");
        var metadata = MessageHeaders.Read(record.Message.Headers);
        metadata.MessageId.Should().NotBeNullOrEmpty();
        metadata.MessageType.Should().Be("TradeOpportunityModel");
        metadata.SchemaVersion.Should().Be("1");
        record.Message.Timestamp.UtcDateTime.Should().BeAfter(before.UtcDateTime).And.BeBefore(DateTime.UtcNow.AddSeconds(2));
        using var stream = new MemoryStream(record.Message.Value);
        Serializer.Deserialize<ArbitrageScanner.Domain.Models.TradeOpportunityModel>(stream).Guid.Should().Be(message.Guid);
    }

    [Fact]
    public async Task Every_event_gets_its_own_message_id()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 1);
        var (service, producer) = Create(kafka.BootstrapServers, topic);
        using var _ = producer;

        await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());
        await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

        kafka.ReadAll(topic, expected: 2).Select(r => MessageHeaders.Read(r.Message.Headers).MessageId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task The_published_counter_tracks_acknowledged_events()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 1);
        var (service, producer) = Create(kafka.BootstrapServers, topic);
        using var _ = producer;
        var published = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaPublishMetrics.MeterName && instrument.Name.EndsWith("published", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref published, value));
        listener.Start();

        await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());
        await service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

        Interlocked.Read(ref published).Should().Be(2);
    }

    [Fact]
    public async Task An_unreachable_broker_fails_the_publish_like_a_broker_outage_failed_it_before()
    {
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
        var (service, producer) = Create("127.0.0.1:1", "spreads");
        using var _ = producer;

        var act = () => service.PostPossiblePosition(TradeOpportunityModelBuilder.Build());

        await act.Should().ThrowAsync<ProduceException<string, byte[]>>();
        Interlocked.Read(ref failures).Should().Be(1);
    }
}
