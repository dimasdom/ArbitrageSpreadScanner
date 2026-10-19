using System.Diagnostics;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

/// <summary>Producer-side trace propagation. Span names and attributes follow the OpenTelemetry messaging
/// semantic conventions. Register with <c>.AddSource(KafkaTelemetry.SourceName)</c>.</summary>
public static class KafkaTelemetry
{
    public const string SourceName = "ArbiScanner.Messaging.Kafka";

    internal static readonly ActivitySource Source = new(SourceName);

    internal static Activity? StartProducer(string topic, string key)
    {
        var activity = Source.StartActivity($"{topic} publish", ActivityKind.Producer);
        if (activity is not null)
        {
            activity.SetTag("messaging.system", "kafka");
            activity.SetTag("messaging.operation.type", "publish");
            activity.SetTag("messaging.destination.name", topic);
            activity.SetTag("messaging.kafka.message.key", key);
        }

        return activity;
    }

    internal static void RecordFailure(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }
}
