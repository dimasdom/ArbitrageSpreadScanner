using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

[Collection(SharedProxyPoolCollection.Name)]
public class ProxyServiceTests
{
    private static async Task<DataService> BuildDataServiceWithProxies(params ProxyModel[] proxies)
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        mockRepo.Setup(r => r.LoadProxies()).ReturnsAsync(proxies.ToList());
        var dataService = new DataService(mockRepo.Object, ServiceFactory.BuildConfig());
        await dataService.LoadProxiesAsync();
        return dataService;
    }

    [Fact]
    public async Task SetNextProxy_WithProxies_AssignsHttpClientAndUserAgentToExchangeServices()
    {
        var dataService = await BuildDataServiceWithProxies(new ProxyModel { ip = "1.1.1.1", port = 8080, username = "u", password = "p" });
        var config = ServiceFactory.BuildConfig();
        var exchangeService = await ServiceFactory.RegisterExchangeService(dataService, config, "binance", "BTC/USDT:USDT");
        var proxyService = new ProxyService(dataService);

        await proxyService.SetNextProxy();

        var exchange = exchangeService.GetExchange();
        exchange.httpClient.Should().NotBeNull();
        exchange.httpClient!.DefaultRequestHeaders.UserAgent.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SetNextProxy_CalledTwice_ReusesHttpClientAcrossRotations()
    {
        var dataService = await BuildDataServiceWithProxies(
            new ProxyModel { ip = "1.1.1.1", port = 8080 },
            new ProxyModel { ip = "2.2.2.2", port = 9090 });
        var config = ServiceFactory.BuildConfig();
        var exchangeService = await ServiceFactory.RegisterExchangeService(dataService, config, "okx", "BTC/USDT:USDT");
        var proxyService = new ProxyService(dataService);

        await proxyService.SetNextProxy();
        var firstClient = exchangeService.GetExchange().httpClient;
        await proxyService.SetNextProxy();
        var secondClient = exchangeService.GetExchange().httpClient;

        firstClient.Should().NotBeNull();
        secondClient.Should().BeSameAs(firstClient);
    }

    [Fact]
    public async Task SetNextProxy_AlsoRotatesObserverExchangeServices()
    {
        var dataService = await BuildDataServiceWithProxies(new ProxyModel { ip = "1.1.1.1", port = 8080 });
        var config = ServiceFactory.BuildConfig();
        var exchangeService = await ServiceFactory.RegisterExchangeService(dataService, config, "kraken", "BTC/USDT:USDT");
        var proxyService = new ProxyService(dataService);

        await proxyService.SetNextProxy();

        dataService.ExchangeObserverServices["kraken"].GetExchange().httpClient.Should().NotBeNull();
    }
}
