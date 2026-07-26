using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ConfigService
    {
        private readonly ConfigModel _config;

        public ConfigModel Current => _config;

        public ConfigService(IConfiguration configuration)
        {
            var config = configuration.GetSection("Arbitrage").Get<ConfigModel>() ?? new ConfigModel();

            var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN");
            if (!string.IsNullOrWhiteSpace(telegramToken))
            {
                config.TelegramToken = telegramToken;
            }

            var chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            if (!string.IsNullOrWhiteSpace(chatId))
            {
                config.ChatId = chatId;
            }

            var mongoConnectionString = Environment.GetEnvironmentVariable("MongoDb_ConnectionString");
            if (!string.IsNullOrWhiteSpace(mongoConnectionString))
            {
                config.MongoDb.ConnectionString = mongoConnectionString;
            }

            var mongoDatabaseName = Environment.GetEnvironmentVariable("MongoDb_DatabaseName");
            if (!string.IsNullOrWhiteSpace(mongoDatabaseName))
            {
                config.MongoDb.DatabaseName = mongoDatabaseName;
            }

            _config = config;
        }
    }
}
