using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Domain.Interfaces;
using ccxt;
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
        private readonly Dictionary<string, RotatingWebProxy> _proxyByExchange = new();

        public ProxyService(DataService dataService)
        {
            _dataService = dataService;
        }

        public Task SetNextProxy()
        {
            var proxies = _dataService.Proxies;
            if (proxies.Count == 0)
            {
                return Task.CompletedTask;
            }

            var proxyToUse = proxies[currentProxyIndex];
            var nextProxy = new WebProxy($"{proxyToUse.ip}:{proxyToUse.port}")
            {
                Credentials = new NetworkCredential(proxyToUse.username, proxyToUse.password)
            };

            foreach (var exchangeService in _dataService.ExchangeServices)
            {
                RotateExchangeProxy(exchangeService.Key, exchangeService.Value.exchange!, nextProxy);
            }

            foreach (var exchangeService in _dataService.ExchangeObserverServices)
            {
                RotateExchangeProxy($"observer:{exchangeService.Key}", exchangeService.Value.exchange!, nextProxy);
            }

            Console.WriteLine($"[{DateTime.Now}] Switched to proxy: {proxyToUse.ip}:{proxyToUse.port}");

            currentProxyIndex = (currentProxyIndex + 1) % proxies.Count;

            return Task.CompletedTask;
        }

        // Reuses one HttpClientHandler/HttpClient per exchange for the process lifetime instead of
        // creating a new pair on every rotation. HttpClientHandler locks Proxy/UseProxy after the
        // first request is sent, so rotation goes through a RotatingWebProxy indirection whose
        // target can change freely without touching the handler's own (locked) configuration.
        private void RotateExchangeProxy(string key, Exchange exchange, WebProxy nextProxy)
        {
            if (_proxyByExchange.TryGetValue(key, out var rotatingProxy))
            {
                rotatingProxy.Rotate(nextProxy);
            }
            else
            {
                rotatingProxy = new RotatingWebProxy(nextProxy);
                _proxyByExchange[key] = rotatingProxy;

                var handler = new HttpClientHandler
                {
                    Proxy = rotatingProxy,
                    UseProxy = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseCookies = false
                };

                var httpClient = new HttpClient(handler, disposeHandler: true);
                httpClient.DefaultRequestHeaders.ConnectionClose = true;
                exchange.httpClient = httpClient;
            }

            exchange.httpClient!.DefaultRequestHeaders.UserAgent.Clear();
            exchange.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GetRandomUserAgent());
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
