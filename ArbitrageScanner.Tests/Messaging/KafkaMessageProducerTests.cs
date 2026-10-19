using Xunit;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArbitrageScanner.Tests.Messaging;

public class KafkaMessageProducerTests
{
    private static DeliveryResult<string, byte[]> Delivered() => new()
    {
        Topic = "spreads",
        Partition = new Partition(2),
        Offset = new Offset(17),
    };

    [Fact]
    public async Task PublishAsync_sends_key_value_and_the_standard_headers()
    {
        var inner = new Mock<IProducer<string, byte[]>>();
        Message<string, byte[]>? sent = null;
        inner
            .Setup(p => p.ProduceAsync("spreads", It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((_, m, _) => sent = m)
            .ReturnsAsync(Delivered());
        var producer = new KafkaMessageProducer(inner.Object, NullLogger<KafkaMessageProducer>.Instance, TimeSpan.FromSeconds(1));

        var result = await producer.PublishAsync("spreads", "k", [9], "TradeOpportunity", messageId: "id-7");

        result.Should().Be(new ProducedMessage("spreads", 2, 17));
        sent!.Key.Should().Be("k");
        sent.Value.Should().Equal(9);
        var metadata = MessageHeaders.Read(sent.Headers);
        metadata.MessageId.Should().Be("id-7");
        metadata.MessageType.Should().Be("TradeOpportunity");
        metadata.SchemaVersion.Should().Be(MessageHeaders.CurrentSchemaVersion);
    }

    [Fact]
    public async Task PublishAsync_generates_a_unique_message_id_when_none_is_given()
    {
        var inner = new Mock<IProducer<string, byte[]>>();
        var ids = new List<string?>();
        inner
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((_, m, _) => ids.Add(MessageHeaders.Read(m.Headers).MessageId))
            .ReturnsAsync(Delivered());
        var producer = new KafkaMessageProducer(inner.Object, NullLogger<KafkaMessageProducer>.Instance, TimeSpan.FromSeconds(1));

        await producer.PublishAsync("spreads", "k", [], "T");
        await producer.PublishAsync("spreads", "k", [], "T");

        ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id));
        ids.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_failed_delivery_surfaces_to_the_caller()
    {
        var inner = new Mock<IProducer<string, byte[]>>();
        inner
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, byte[]>(new Error(ErrorCode.Local_MsgTimedOut), Delivered()));
        var producer = new KafkaMessageProducer(inner.Object, NullLogger<KafkaMessageProducer>.Instance, TimeSpan.FromSeconds(1));

        var act = () => producer.PublishAsync("spreads", "k", [], "T");

        await act.Should().ThrowAsync<ProduceException<string, byte[]>>();
    }

    [Fact]
    public void Dispose_flushes_before_releasing_the_producer()
    {
        var inner = new Mock<IProducer<string, byte[]>>(MockBehavior.Strict);
        var order = new List<string>();
        inner.Setup(p => p.Flush(It.IsAny<TimeSpan>())).Callback(() => order.Add("flush")).Returns(0);
        inner.Setup(p => p.Dispose()).Callback(() => order.Add("dispose"));

        new KafkaMessageProducer(inner.Object, NullLogger<KafkaMessageProducer>.Instance, TimeSpan.FromSeconds(1)).Dispose();

        order.Should().Equal("flush", "dispose");
    }
}
