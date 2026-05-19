using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
using ccxt;
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
        private readonly DataService _dataService;
        private IConnection? _connection;
        private IChannel? _publishChannel;
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private readonly SemaphoreSlim _publishSemaphore = new(1, 1);
        private bool _topologyDeclared;

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
                    _publishChannel = await _connection.CreateChannelAsync();
                }

                if (!_topologyDeclared)
                {
                    await _publishChannel.ExchangeDeclareAsync("spread_fanout_exchange", ExchangeType.Fanout, durable: true);
                    await _publishChannel.QueueDeclareAsync("spread_telegram", durable: false, exclusive: false, autoDelete: false);
                    await _publishChannel.QueueBindAsync("spread_telegram", "spread_fanout_exchange", routingKey: "");

                    await _publishChannel.QueueDeclareAsync("spread_api", durable: false, exclusive: false, autoDelete: false);
                    await _publishChannel.QueueBindAsync("spread_api", "spread_fanout_exchange", routingKey: "");

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
                await EnsureInitializedAsync();
                tradeOpportunity.FormatOrdersToSend();

                using var ms = new MemoryStream();
                Serializer.Serialize(ms, tradeOpportunity);
                var body = ms.ToArray();

                await _publishSemaphore.WaitAsync();
                try
                {
                    await _publishChannel!.BasicPublishAsync("spread_fanout_exchange", routingKey: "", mandatory: false, body: body);
                }
                finally
                {
                    _publishSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, tradeOpportunity.ExchangeLong?.Symbol ?? "", "PostPossiblePositionFanout");
                Console.WriteLine(ex.Message);
            }
        }
    }
}
