using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ccxt;
using Polly;
using Polly.Retry;
using ProtoBuf;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ServicesCommunicationService : IServicesCommunicationService
    {
        public const string FanoutExchange = "spread_fanout_exchange";
        public const string DeadLetterExchange = "spread_dlx";

        private readonly DataService _dataService;
        private IConnection? _connection;
        private IChannel? _publishChannel;
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private readonly SemaphoreSlim _publishSemaphore = new(1, 1);
        private bool _topologyDeclared;
        private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();

        private readonly MessagingPublishOptions _publish;
        private readonly IKafkaMessageProducer? _kafka;
        private readonly string _kafkaTopic;
        private readonly ILogger<ServicesCommunicationService> _logger;

        /// <param name="publishOptions">Which brokers to publish to; defaults to RabbitMQ only.</param>
        /// <param name="kafka">Required when <c>Messaging:Publish:Kafka</c> is on.</param>
        public ServicesCommunicationService(
            DataService dataService,
            IOptions<MessagingPublishOptions>? publishOptions = null,
            IKafkaMessageProducer? kafka = null,
            IOptions<KafkaOptions>? kafkaOptions = null,
            ILogger<ServicesCommunicationService>? logger = null)
        {
            _dataService = dataService;
            _publish = publishOptions?.Value ?? new MessagingPublishOptions();
            _kafka = kafka;
            _kafkaTopic = kafkaOptions?.Value.SpreadsTopic ?? new KafkaOptions().SpreadsTopic;
            _logger = logger ?? NullLogger<ServicesCommunicationService>.Instance;
        }

        private async Task EnsureInitializedAsync()
        {
            if (_connection is not null && _publishChannel is not null && _topologyDeclared)
            {
                return;
            }

            await _initSemaphore.WaitAsync();
            try
            {
                if (_connection is null)
                {
                    var factory = ArbitrageScanner.Infrastructure.Common.RabbitMqConnectionFactory.FromEnvironment();
                    _connection = await factory.CreateConnectionAsync();
                }

                if (_publishChannel is null)
                {
                    _publishChannel = await _connection.CreateChannelAsync(
                        new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
                }

                if (!_topologyDeclared)
                {
                    await _publishChannel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true);

                    var dlxArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };

                    await _publishChannel.ExchangeDeclareAsync(FanoutExchange, ExchangeType.Fanout, durable: true);
                    await _publishChannel.QueueDeclareAsync("spread_telegram", durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs);
                    await _publishChannel.QueueBindAsync("spread_telegram", FanoutExchange, routingKey: "");

                    await _publishChannel.QueueDeclareAsync("spread_api", durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs);
                    await _publishChannel.QueueBindAsync("spread_api", FanoutExchange, routingKey: "");

                    _topologyDeclared = true;
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

          public async Task PostPossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
           await PostPossiblePositionFanout(tradeOpportunity);
        }

          public async Task PostPossiblePositionFanout(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                tradeOpportunity.FormatOrdersToSend();

                using var ms = new MemoryStream();
                Serializer.Serialize(ms, tradeOpportunity);
                var body = ms.ToArray();

                // One identity and one timestamp for the event, shared by both brokers, so consumers can dedupe on the
                // message-id and the parity check can match the two deliveries. Seconds precision: AMQP timestamps have no more.
                var messageId = Guid.NewGuid().ToString();
                var producedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                if (_publish.RabbitMq)
                {
                    await _retryPipeline.ExecuteAsync(async ct =>
                    {
                        await EnsureInitializedAsync();

                        var properties = new BasicProperties
                        {
                            MessageId = messageId,
                            Timestamp = new AmqpTimestamp(producedAt.ToUnixTimeSeconds()),
                            Type = SpreadEventKeys.MessageType,
                        };

                        await _publishSemaphore.WaitAsync(ct);
                        try
                        {
                            await _publishChannel!.BasicPublishAsync(FanoutExchange, routingKey: "", mandatory: false, basicProperties: properties, body: body, cancellationToken: ct);
                        }
                        finally
                        {
                            _publishSemaphore.Release();
                        }
                    });
                }

                if (_publish.Kafka)
                {
                    await PublishToKafkaAsync(tradeOpportunity, body, messageId, producedAt);
                }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostPossiblePositionFanout");
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        private async Task PublishToKafkaAsync(TradeOpportunityModel tradeOpportunity, byte[] body, string messageId, DateTimeOffset producedAt)
        {
            if (_kafka is null)
            {
                if (!_publish.RabbitMq)
                {
                    throw new InvalidOperationException("Messaging:Publish:Kafka is on but no Kafka producer is registered.");
                }

                _logger.LogError("Messaging:Publish:Kafka is on but no Kafka producer is registered; skipping Kafka publish");
                return;
            }

            string key;
            Task<ProducedMessage> publish;
            try
            {
                key = SpreadEventKeys.For(tradeOpportunity);
                publish = _kafka.PublishAsync(_kafkaTopic, key, body, SpreadEventKeys.MessageType, messageId, producedAt);
            }
            catch (Exception ex) when (_publish.RabbitMq)
            {
                RecordShadowFailure(ex, tradeOpportunity.Guid, messageId);
                return;
            }

            if (_publish.RabbitMq)
            {
                // Shadow: RabbitMQ is authoritative and already succeeded. Kafka must not slow the observers down (a dead
                // broker would otherwise stall them for the whole delivery timeout) or fail them: observe the result in the
                // background. Produce order per key is still the call order, because the enqueue happens here, synchronously.
                _ = ObserveShadowPublishAsync(publish, tradeOpportunity.Guid, messageId);
                return;
            }

            await publish;
            KafkaPublishMetrics.Published.Add(1);
        }

        private async Task ObserveShadowPublishAsync(Task<ProducedMessage> publish, Guid spreadGuid, string messageId)
        {
            try
            {
                await publish;
                KafkaPublishMetrics.Published.Add(1);
            }
            catch (Exception ex)
            {
                RecordShadowFailure(ex, spreadGuid, messageId);
            }
        }

        private void RecordShadowFailure(Exception ex, Guid spreadGuid, string messageId)
        {
            KafkaPublishMetrics.Failures.Add(1);
            _logger.LogWarning(ex, "Shadow Kafka publish failed (RabbitMQ unaffected). Spread={SpreadGuid} MessageId={MessageId}", spreadGuid, messageId);
        }
    }
}
