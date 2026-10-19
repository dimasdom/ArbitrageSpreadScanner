using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

public sealed record ProducedMessage(string Topic, int Partition, long Offset);

/// <summary>Thin abstraction over the (thread-safe, expensive) Kafka producer. Register as a singleton.</summary>
public interface IKafkaMessageProducer
{
    /// <summary>Publishes with the standard headers (<c>traceparent</c>, <c>message-type</c>, <c>schema-version</c>,
    /// <c>message-id</c>) and completes once the broker acknowledged the write. Throws if it cannot be delivered.</summary>
    Task<ProducedMessage> PublishAsync(
        string topic,
        string key,
        byte[] value,
        string messageType,
        string? messageId = null,
        DateTimeOffset? producedAt = null,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaMessageProducer : IKafkaMessageProducer, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly ILogger<KafkaMessageProducer> _logger;
    private readonly TimeSpan _flushTimeout;

    public KafkaMessageProducer(IOptions<KafkaOptions> options, ILogger<KafkaMessageProducer> logger)
        : this(BuildProducer(options.Value, logger), logger, TimeSpan.FromSeconds(options.Value.FlushTimeoutSeconds))
    {
    }

    internal KafkaMessageProducer(IProducer<string, byte[]> producer, ILogger<KafkaMessageProducer> logger, TimeSpan flushTimeout)
    {
        _producer = producer;
        _logger = logger;
        _flushTimeout = flushTimeout;
    }

    public async Task<ProducedMessage> PublishAsync(
        string topic,
        string key,
        byte[] value,
        string messageType,
        string? messageId = null,
        DateTimeOffset? producedAt = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = KafkaTelemetry.StartProducer(topic, key);
        var id = messageId ?? Guid.NewGuid().ToString();
        activity?.SetTag("messaging.message.id", id);

        var message = new Message<string, byte[]>
        {
            Key = key,
            Value = value,
            Headers = MessageHeaders.Create(messageType, id, activity),
            Timestamp = producedAt is { } at ? new Timestamp(at) : Timestamp.Default,
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            return new ProducedMessage(result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            KafkaTelemetry.RecordFailure(activity, ex);
            _logger.LogError(ex, "Kafka produce failed. Topic={Topic} Key={Key} Reason={Reason}", topic, key, ex.Error.Reason);
            throw;
        }
    }

    /// <summary>Flushes in-flight messages so a graceful shutdown does not drop acknowledged-but-unsent records.</summary>
    public void Dispose()
    {
        try
        {
            _producer.Flush(_flushTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka producer flush failed during shutdown");
        }

        _producer.Dispose();
    }

    private static IProducer<string, byte[]> BuildProducer(KafkaOptions options, ILogger logger)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = options.LingerMs,
            CompressionType = CompressionType.Lz4,
            MessageTimeoutMs = options.DeliveryTimeoutMs,
        };

        return new ProducerBuilder<string, byte[]>(config)
            .SetErrorHandler((_, error) => logger.LogWarning("Kafka producer error: {Reason} (fatal={Fatal})", error.Reason, error.IsFatal))
            .Build();
    }
}
