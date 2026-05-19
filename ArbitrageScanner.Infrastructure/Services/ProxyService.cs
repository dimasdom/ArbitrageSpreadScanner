using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
using MongoDB.Driver;
using System.Net;

namespace ArbitrageScanner.Infrastructure.Services
{
    public class ProxyService : IProxyService
    {
        private static readonly string[] Platforms =
        {
            "Windows NT 10.0; Win64; x64",
            "Macintosh; Intel Mac OS X 10_15_7",
            "X11; Linux x86_64",
            "Windows NT 6.1; Win64; x64",
            "iPhone; CPU iPhone OS 14_6 like Mac OS X",
            "Android 10; Mobile"
        };

        private static readonly string[] Browsers =
        {
            "Chrome", "Firefox", "Edge", "Safari"
        };

        private readonly DataService _dataService;
        private int currentProxyIndex = 0;
        private readonly Random _rand = new();

        public ProxyService(DataService dataService)
        {
            _dataService = dataService;
        }

        public async Task SetNextProxy()
        {
            var proxies = _dataService.Proxies;
            if (proxies.Count == 0)
            {
                return;
            }

            var proxyToUse = proxies[currentProxyIndex];


            foreach (var exchangeService in _dataService.ExchangeServices)
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"{proxyToUse.ip}:{proxyToUse.port}")
                    {
                        Credentials = new NetworkCredential(proxyToUse.username, proxyToUse.password)
                    },
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                handler.UseCookies = false;
                var httpClient = new HttpClient(handler, true);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GetRandomUserAgent());
                httpClient.DefaultRequestHeaders.ConnectionClose = true;
                // Swap first, then dispose after a grace period so in-flight requests finish cleanly.
                // Immediate dispose kills active connections and causes cascading timeouts.
                var oldClient = exchangeService.Value.exchange!.httpClient;
                exchangeService.Value.exchange.httpClient = httpClient;
                _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => oldClient?.Dispose());
            }

            foreach (var exchangeService in _dataService.ExchangeObserverServices)
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"{proxyToUse.ip}:{proxyToUse.port}")
                    {
                        Credentials = new NetworkCredential(proxyToUse.username, proxyToUse.password)
                    },
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                handler.UseCookies = false;
                var httpClient = new HttpClient(handler, true);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GetRandomUserAgent());
                httpClient.DefaultRequestHeaders.ConnectionClose = true;
                var oldClient = exchangeService.Value.exchange!.httpClient;
                exchangeService.Value.exchange.httpClient = httpClient;
                _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => oldClient?.Dispose());
            }

            Console.WriteLine($"[{DateTime.Now}] Switched to proxy: {proxyToUse.ip}:{proxyToUse.port}");

            currentProxyIndex = (currentProxyIndex + 1) % proxies.Count;

            await Task.CompletedTask;
        }

        private string GetRandomUserAgent()
        {
            var platform = Platforms[_rand.Next(Platforms.Length)];
            var browser = Browsers[_rand.Next(Browsers.Length)];

            return browser switch
            {
                "Chrome" => $"Mozilla/5.0 ({platform}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{RandomVersion(100, 122)}.0.{_rand.Next(1000, 5000)}.{_rand.Next(10, 100)} Safari/537.36",
                "Firefox" => $"Mozilla/5.0 ({platform}; rv:{RandomVersion(90, 115)}.0) Gecko/20100101 Firefox/{RandomVersion(90, 115)}.0",
                "Edge" => $"Mozilla/5.0 ({platform}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{RandomVersion(100, 122)}.0.{_rand.Next(1000, 5000)}.{_rand.Next(10, 100)} Safari/537.36 Edg/{RandomVersion(100, 122)}.0",
                "Safari" => $"Mozilla/5.0 ({platform}) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/{RandomVersion(13, 17)}.0 Safari/605.1.15",
                _ => "Mozilla/5.0"
            };
        }

        private string RandomVersion(int min, int max)
        {
            return _rand.Next(min, max).ToString();
        }
    }
}
