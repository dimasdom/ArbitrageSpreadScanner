using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ArbitrageScanner.Infrastructure.Extensions
{
    public static class ConfigurationExtensions
    {
        public static ConfigModel GetArbitrageConfig(this IConfiguration configuration)
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

            // Comma-separated, e.g. "Binance,Bybit,MEXC" - keeps the .env entry to one
            // readable line instead of the indexed Arbitrage__ExchangeList__0/__1/... form.
            var exchangeList = Environment.GetEnvironmentVariable("ARBITRAGE_EXCHANGE_LIST");
            if (!string.IsNullOrWhiteSpace(exchangeList))
            {
                config.ExchangeList = SplitList(exchangeList);
            }

            // JSON array of {ip, port, country_code, username, password} - the proxy list is
            // structured data, so unlike the two lists above a flat CSV isn't a good fit.
            var proxyListJson = Environment.GetEnvironmentVariable("ARBITRAGE_PROXY_LIST");
            if (!string.IsNullOrWhiteSpace(proxyListJson))
            {
                var proxies = JsonSerializer.Deserialize<List<ProxyModel>>(proxyListJson);
                if (proxies is not null)
                {
                    config.ProxyList = proxies;
                }
            }

            return config;
        }

        private static List<string> SplitList(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
