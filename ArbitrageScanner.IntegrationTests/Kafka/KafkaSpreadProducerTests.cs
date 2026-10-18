using System.Diagnostics;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using ArbitrageScanner.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace ArbitrageScanner.IntegrationTests.Kafka;

[Collection(KafkaProducerCollection.Name)]
public sealed class KafkaSpreadProducerTests(KafkaProducerTestFixture kafka)
{
    private KafkaMessageProducer CreateProducer() =>
        new(Options.Create(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, LingerMs = 0 }), NullLogger<KafkaMessageProducer>.Instance);

    private static byte[] Serialize(TradeOpportunityModel model)
    {
        model.FormatOrdersToSend();
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, model);
        return stream.ToArray();
    }

    [Fact]
    public async Task Published_spread_round_trips_with_the_standard_headers_and_trace_context()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 3);
        using var source = new ActivitySource("test.parent");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var model = TradeOpportunityModelBuilder.Build(type: SpreadType.Futures, actionType: OrderStatus.Open);

        using var producer = CreateProducer();
        using (source.StartActivity("detect"))
        {
            await producer.PublishAsync(topic, SpreadEventKeys.For(model), Serialize(model), SpreadEventKeys.MessageType, messageId: "msg-1");
        }

        var record = kafka.ReadAll(topic, expected: 1).Should().ContainSingle().Subject;
        record.Message.Key.Should().Be("futures:binance:bybit:BTC-USDT");

        using var stream = new MemoryStream(record.Message.Value);
        var received = Serializer.Deserialize<TradeOpportunityModel>(stream);
        received.Guid.Should().Be(model.Guid);
        received.ExchangeRateA!.Exchange.Should().Be("Binance");

        var metadata = MessageHeaders.Read(record.Message.Headers);
        metadata.MessageId.Should().Be("msg-1");
        metadata.MessageType.Should().Be("TradeOpportunityModel");
        metadata.SchemaVersion.Should().Be("1");
        metadata.TraceParent.Should().MatchRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");
    }

    [Fact]
    public async Task A_spreads_lifecycle_stays_on_one_partition_in_order_even_when_long_and_short_flip()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 6);
        using var producer = CreateProducer();
        var guid = Guid.NewGuid();
        const int updates = 30;

        var lifecycle = new List<TradeOpportunityModel> { TradeOpportunityModelBuilder.Build(guid: guid, actionType: OrderStatus.Open, spread: 0) };
        for (var i = 1; i <= updates; i++)
        {
            var update = TradeOpportunityModelBuilder.Build(guid: guid, actionType: OrderStatus.Update, spread: i);
            if (i % 2 == 0)
            {
                // The spread changed sign: long and short swap sides but the opportunity is the same.
                (update.ExchangeLong, update.ExchangeShort) = (update.ExchangeShort, update.ExchangeLong);
            }

            lifecycle.Add(update);
        }

        lifecycle.Add(TradeOpportunityModelBuilder.Build(guid: guid, actionType: OrderStatus.Close, spread: 0));

        foreach (var message in lifecycle)
        {
            await producer.PublishAsync(topic, SpreadEventKeys.For(message), Serialize(message), SpreadEventKeys.MessageType);
        }

        var records = kafka.ReadAll(topic, expected: lifecycle.Count);

        records.Should().HaveCount(lifecycle.Count);
        records.Select(r => r.Message.Key).Distinct().Should().ContainSingle();
        records.Select(r => r.Partition.Value).Distinct().Should().ContainSingle("one key maps to one partition");
        var spreads = records.OrderBy(r => r.Offset.Value).Select(r =>
        {
            using var stream = new MemoryStream(r.Message.Value);
            return Serializer.Deserialize<TradeOpportunityModel>(stream).Spread;
        });
        spreads.Should().Equal(lifecycle.Select(m => m.Spread), "offsets must follow publish order");
    }

    [Fact]
    public async Task Different_spreads_are_spread_across_partitions()
    {
        var topic = await kafka.CreateTopicAsync(partitions: 6);
        using var producer = CreateProducer();

        for (var i = 0; i < 40; i++)
        {
            var model = TradeOpportunityModelBuilder.Build(symbol: $"COIN{i}/USDT");
            await producer.PublishAsync(topic, SpreadEventKeys.For(model), Serialize(model), SpreadEventKeys.MessageType);
        }

        kafka.ReadAll(topic, expected: 40).Select(r => r.Partition.Value).Distinct().Count().Should().BeGreaterThan(1);
    }
}
