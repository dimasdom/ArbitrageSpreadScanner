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
}
