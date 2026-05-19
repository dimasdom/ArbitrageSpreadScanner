using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ConfigService
    {
        private static ConfigModel _config = new ConfigModel();

        public static ConfigModel Config => _config;
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

            _config = config;
        }
    }
}
