namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

/// <summary>Producer settings, bound from the <c>Kafka</c> configuration section
/// (env vars: <c>Kafka__BootstrapServers</c>, ...).</summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "arbiscanner-arbitrage-scanner";

    public string SpreadsTopic { get; set; } = "spreads";

    /// <summary>Upper bound for a produce call including retries; past it the call fails instead of blocking the observer loops.</summary>
    public int DeliveryTimeoutMs { get; set; } = 30_000;

    public int LingerMs { get; set; } = 5;

    public int FlushTimeoutSeconds { get; set; } = 10;
}
