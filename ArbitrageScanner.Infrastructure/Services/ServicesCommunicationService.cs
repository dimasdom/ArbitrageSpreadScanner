using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
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

        public ServicesCommunicationService(DataService dataService)
        {
            _dataService = dataService;
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
                    var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
                    var factory = new ConnectionFactory { HostName = host };
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
                await _retryPipeline.ExecuteAsync(async ct =>
                {
                    await EnsureInitializedAsync();
                    tradeOpportunity.FormatOrdersToSend();

                    using var ms = new MemoryStream();
                    Serializer.Serialize(ms, tradeOpportunity);
                    var body = ms.ToArray();

                    await _publishSemaphore.WaitAsync(ct);
                    try
                    {
                        await _publishChannel!.BasicPublishAsync(FanoutExchange, routingKey: "", mandatory: false, body: body, cancellationToken: ct);
                    }
                    finally
                    {
                        _publishSemaphore.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostPossiblePositionFanout");
                Console.WriteLine(ex.Message);
                throw;
            }
        }
    }
}
