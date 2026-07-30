using ArbitrageScanner.Infrastructure.Extensions;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

[Collection(SharedEnvironmentVariablesCollection.Name)]
public class ConfigurationExtensionsTests
{
    private static IConfiguration BuildConfig(double positionSize = 500) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arbitrage:PositionSize"] = positionSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        .Build();

    private static void ClearEnvVars()
    {
        Environment.SetEnvironmentVariable("TELEGRAM_TOKEN", null);
        Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", null);
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        Environment.SetEnvironmentVariable("ARBITRAGE_EXCHANGE_LIST", null);
        Environment.SetEnvironmentVariable("ARBITRAGE_PROXY_LIST", null);
    }

    [Fact]
    public void GetArbitrageConfig_AllEnvVarsSet_OverridesConfigValues()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("TELEGRAM_TOKEN", "env-token");
        Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", "env-chat-id");
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://env:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", "EnvDb");
        try
        {
            var config = BuildConfig().GetArbitrageConfig();

            config.TelegramToken.Should().Be("env-token");
            config.ChatId.Should().Be("env-chat-id");
            config.MongoDb.ConnectionString.Should().Be("mongodb://env:27017");
            config.MongoDb.DatabaseName.Should().Be("EnvDb");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetArbitrageConfig_NoEnvVarsSet_LeavesConfigValuesUntouched()
    {
        ClearEnvVars();

        var config = BuildConfig().GetArbitrageConfig();

        config.TelegramToken.Should().BeNull();
        config.ChatId.Should().BeNull();
    }

    [Fact]
    public void GetArbitrageConfig_ExchangeListEnvVarsSet_OverridesConfigValues()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("ARBITRAGE_EXCHANGE_LIST", "Binance, Bybit,MEXC");
        try
        {
            var config = BuildConfig().GetArbitrageConfig();

            config.ExchangeList.Should().Equal("Binance", "Bybit", "MEXC");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetArbitrageConfig_ProxyListEnvVarSet_OverridesConfigValues()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable(
            "ARBITRAGE_PROXY_LIST",
            """[{"ip":"proxy.example.com","port":1234,"country_code":"CH","username":"user0","password":"pass0"}]""");
        try
        {
            var config = BuildConfig().GetArbitrageConfig();

            config.ProxyList.Should().ContainSingle();
            config.ProxyList[0].ip.Should().Be("proxy.example.com");
            config.ProxyList[0].port.Should().Be(1234);
            config.ProxyList[0].country_code.Should().Be("CH");
            config.ProxyList[0].username.Should().Be("user0");
            config.ProxyList[0].password.Should().Be("pass0");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetArbitrageConfig_NoListEnvVarsSet_LeavesListsUntouched()
    {
        ClearEnvVars();

        var config = BuildConfig().GetArbitrageConfig();

        config.ExchangeList.Should().BeEmpty();
        config.ProxyList.Should().BeEmpty();
    }
}
