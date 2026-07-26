using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class FileServiceTests
{
    [Fact]
    public void LoadConfig_ReturnsBoundArbitrageConfig()
    {
        var service = new FileService(ServiceFactory.BuildConfig(positionSize: 42));

        service.LoadConfig().PositionSize.Should().Be(42);
    }

    [Fact]
    public void LoadCurrentConfig_ReturnsSameConfigAsLoadConfig()
    {
        var service = new FileService(ServiceFactory.BuildConfig(positionSize: 42));

        service.LoadCurrentConfig().Should().BeSameAs(service.LoadConfig());
    }

    [Fact]
    public void LoadExchangeList_ReturnsConfiguredExchanges()
    {
        var service = new FileService(ServiceFactory.BuildConfig());

        service.LoadExchangeList().Should().BeEmpty();
    }

    [Fact]
    public void LoadCurrentExchangeList_ReturnsSameListAsLoadExchangeList()
    {
        var service = new FileService(ServiceFactory.BuildConfig());

        service.LoadCurrentExchangeList().Should().BeSameAs(service.LoadExchangeList());
    }

    [Fact]
    public void LoadProxyList_ReturnsConfiguredProxies()
    {
        var service = new FileService(ServiceFactory.BuildConfig());

        service.LoadProxyList().Should().BeEmpty();
    }

    [Fact]
    public void LoadCurrentProxyList_ReturnsSameListAsLoadProxyList()
    {
        var service = new FileService(ServiceFactory.BuildConfig());

        service.LoadCurrentProxyList().Should().BeSameAs(service.LoadProxyList());
    }
}
