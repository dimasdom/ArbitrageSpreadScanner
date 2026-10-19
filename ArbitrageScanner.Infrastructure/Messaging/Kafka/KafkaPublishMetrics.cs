using System.Diagnostics.Metrics;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

/// <summary>Publish-side counters. Register with <c>.AddMeter(KafkaPublishMetrics.MeterName)</c>.</summary>
public static class KafkaPublishMetrics
{
    public const string MeterName = "ArbiScanner.Messaging.Publish";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "arbiscanner.messaging.publish.kafka.published",
        description: "Spread events acknowledged by Kafka.");

    public static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "arbiscanner.messaging.publish.kafka.failures",
        description: "Spread events Kafka did not acknowledge. In shadow mode these are swallowed; this counter is how they are seen.");
}
