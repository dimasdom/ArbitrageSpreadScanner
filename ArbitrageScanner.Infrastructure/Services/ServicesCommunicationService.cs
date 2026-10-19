using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace ArbitrageScanner.Infrastructure.Services
{
    /// <summary>Publishes spread events (Open / Update / Close) to the Kafka <c>spreads</c> topic.</summary>
    public class ServicesCommunicationService(
        DataService dataService,
        IKafkaMessageProducer kafka,
        IOptions<KafkaOptions> kafkaOptions) : IServicesCommunicationService
    {
        private readonly string _topic = kafkaOptions.Value.SpreadsTopic;

        /// <summary>Completes once Kafka acknowledged the event; throws if it could not be delivered. Events for one spread share a
        /// key, so the broker keeps them in publish order.</summary>
        public async Task PostPossiblePosition(TradeOpportunityModel tradeOpportunity)
        {
            try
            {
                tradeOpportunity.FormatOrdersToSend();

                using var ms = new MemoryStream();
                Serializer.Serialize(ms, tradeOpportunity);

                // Seconds precision keeps the timestamp stable under any later re-encoding.
                var producedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                await kafka.PublishAsync(
                    _topic,
                    SpreadEventKeys.For(tradeOpportunity),
                    ms.ToArray(),
                    SpreadEventKeys.MessageType,
                    messageId: Guid.NewGuid().ToString(),
                    producedAt: producedAt);

                KafkaPublishMetrics.Published.Add(1);
            }
            catch (Exception ex)
            {
                KafkaPublishMetrics.Failures.Add(1);
                dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", nameof(PostPossiblePosition));
                Console.WriteLine(ex.Message);
                throw;
            }
        }
    }
}
