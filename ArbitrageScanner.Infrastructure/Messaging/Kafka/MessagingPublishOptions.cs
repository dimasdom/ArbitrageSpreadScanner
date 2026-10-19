namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

/// <summary>Which brokers spread events are published to during the RabbitMQ -> Kafka migration. Bound from
/// <c>Messaging:Publish</c> (env vars <c>Messaging__Publish__RabbitMq</c>, <c>Messaging__Publish__Kafka</c>).</summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>RabbitMq=true, Kafka=false</c>: today's behaviour (default).</item>
/// <item><c>RabbitMq=true, Kafka=true</c>: shadow dual publish. RabbitMQ remains authoritative: Kafka is published
/// after RabbitMQ succeeded, is never awaited and can never throw into the observers.</item>
/// <item><c>RabbitMq=false, Kafka=true</c>: cut over. Kafka is authoritative: awaited, and failures propagate like RabbitMQ's do today.</item>
/// </list>
/// </remarks>
public sealed class MessagingPublishOptions
{
    public const string SectionName = "Messaging:Publish";

    public bool RabbitMq { get; set; } = true;

    public bool Kafka { get; set; }
}
