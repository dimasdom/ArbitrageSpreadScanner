using Xunit;
using System.Diagnostics;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using Confluent.Kafka;
using FluentAssertions;

namespace ArbitrageScanner.Tests.Messaging;

public class MessageHeadersTests
{
    [Fact]
    public void Create_sets_the_four_mandatory_headers_without_an_activity()
    {
        var headers = MessageHeaders.Create("TradeOpportunity", "id-1", activity: null);

        var metadata = MessageHeaders.Read(headers);
        metadata.MessageId.Should().Be("id-1");
        metadata.MessageType.Should().Be("TradeOpportunity");
        metadata.SchemaVersion.Should().Be(MessageHeaders.CurrentSchemaVersion);
        metadata.TraceParent.Should().BeNull();
        metadata.TraceState.Should().BeNull();
    }

    [Fact]
    public void Create_injects_w3c_trace_context_from_the_activity()
    {
        using var source = new ActivitySource("test.headers");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "test.headers",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("produce", ActivityKind.Producer)!;
        activity.TraceStateString = "vendor=1";

        var headers = MessageHeaders.Create("TradeOpportunity", "id-2", activity);

        var metadata = MessageHeaders.Read(headers);
        metadata.TraceParent.Should().Be(activity.Id);
        metadata.TraceParent.Should().MatchRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");
        metadata.TraceState.Should().Be("vendor=1");
    }

    [Fact]
    public void Read_tolerates_missing_headers()
    {
        MessageHeaders.Read(null).Should().Be(new EnvelopeMetadata(null, null, null, null, null));
        MessageHeaders.Read(new Headers()).MessageId.Should().BeNull();
    }

    [Fact]
    public void Read_uses_the_last_value_when_a_header_repeats()
    {
        var headers = new Headers { { MessageHeaders.MessageId, "a"u8.ToArray() }, { MessageHeaders.MessageId, "b"u8.ToArray() } };

        MessageHeaders.Read(headers).MessageId.Should().Be("b");
    }
}
