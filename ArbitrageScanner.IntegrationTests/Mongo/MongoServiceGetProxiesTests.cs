using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Xunit;

namespace ArbitrageScanner.IntegrationTests.Mongo;

[Collection(MongoCollection.Name)]
public class MongoServiceGetProxiesTests(MongoTestFixture fixture)
{
    private static FileService BuildFileService(params ProxyModel[] proxies)
    {
        var data = new Dictionary<string, string?>();
        for (var i = 0; i < proxies.Length; i++)
        {
            data[$"Arbitrage:ProxyList:{i}:ip"] = proxies[i].ip;
            data[$"Arbitrage:ProxyList:{i}:port"] = proxies[i].port.ToString();
            data[$"Arbitrage:ProxyList:{i}:country_code"] = proxies[i].country_code;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        return new FileService(config);
    }

    private async Task ClearProxiesCollectionAsync()
    {
        var collection = fixture.Database.GetCollection<ProxyModel>("Proxies");
        await collection.DeleteManyAsync(Builders<ProxyModel>.Filter.Empty);
    }

    [Fact]
    public async Task GetProxies_EmptyCollectionAndNoFileService_ReturnsEmpty()
    {
        await ClearProxiesCollectionAsync();
        var mongoService = new MongoService(fixture.ConnectionString, MongoTestFixture.DatabaseName);

        var proxies = await mongoService.GetProxies();

        proxies.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProxies_EmptyCollectionWithFileServiceProxies_InsertsAndReturnsThem()
    {
        await ClearProxiesCollectionAsync();
        var marker = Guid.NewGuid().ToString("N");
        var seedIp = $"10.1.2.3-{marker}";
        var fileService = BuildFileService(new ProxyModel { ip = seedIp, port = 1080, country_code = "US" });
        var mongoService = new MongoService(fixture.ConnectionString, MongoTestFixture.DatabaseName, fileService);

        var proxies = (await mongoService.GetProxies()).ToList();

        proxies.Should().ContainSingle(p => p.ip == seedIp && p.port == 1080);

        var collection = fixture.Database.GetCollection<ProxyModel>("Proxies");
        var stored = await collection.Find(p => p.ip == seedIp).FirstOrDefaultAsync();
        stored.Should().NotBeNull();
    }
}
