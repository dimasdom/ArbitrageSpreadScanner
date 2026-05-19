using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace ArbitrageScanner.Infrastructure.Services

{
    public class TelegramNotifierService : ITelegramNotifierService
    {
        private readonly DataService _dataService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConfigModel _config;

        public TelegramNotifierService(IConfiguration configuration, DataService dataService, IHttpClientFactory httpClientFactory)
        {
            _config = configuration.GetArbitrageConfig();
            _dataService = dataService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task SendMessageAsync(string message)
        {
            var botToken = _config.TelegramToken;
            var chatId = _config.ChatId;

            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }

            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var data = new Dictionary<string, string>
            {
                { "chat_id", chatId },
                { "text", message }
            };

            try
            {
                var httpClient = _httpClientFactory.CreateClient("Telegram");
                using var content = new FormUrlEncodedContent(data);
                var response = await httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    var details = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Telegram send failed. StatusCode={(int)response.StatusCode}, Response={details}");
                }
            }
            catch (Exception ex)
            {
                _dataService.LogErrorEntry(ex, method: "TelegramNotifierService.SendMessageAsync");
                Console.WriteLine($"Telegram send exception: {ex.Message}");
            }
        }
    }
}
