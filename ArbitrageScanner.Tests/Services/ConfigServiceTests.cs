using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

[Collection(SharedEnvironmentVariablesCollection.Name)]
public class ConfigServiceTests
{
    private static IConfiguration BuildConfig(double positionSize = 500) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Arbitrage:PositionSize"] = positionSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        .Build();

    [Fact]
    public void Current_ReadsFromArbitrageSection()
    {
        var service = new ConfigService(BuildConfig(1234));

        service.Current.PositionSize.Should().Be(1234);
    }

    [Fact]
    public void Current_TelegramTokenEnvVar_OverridesConfig()
    {
        Environment.SetEnvironmentVariable("TELEGRAM_TOKEN", "env-token");
        try
        {
            var service = new ConfigService(BuildConfig());

            service.Current.TelegramToken.Should().Be("env-token");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_TOKEN", null);
        }
    }

    [Fact]
    public void Current_ChatIdEnvVar_OverridesConfig()
    {
        Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", "12345");
        try
        {
            var service = new ConfigService(BuildConfig());

            service.Current.ChatId.Should().Be("12345");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", null);
        }
    }

    [Fact]
    public void Current_MongoEnvVars_OverrideMongoConfig()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://test:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", "TestDb");
        try
        {
            var service = new ConfigService(BuildConfig());

            service.Current.MongoDb.ConnectionString.Should().Be("mongodb://test:27017");
            service.Current.MongoDb.DatabaseName.Should().Be("TestDb");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
            Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        }
    }

    [Fact]
    public void Current_NoEnvVars_LeavesConfigValuesUntouched()
    {
        Environment.SetEnvironmentVariable("TELEGRAM_TOKEN", null);
        Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", null);

        var service = new ConfigService(BuildConfig());

        service.Current.TelegramToken.Should().BeNull();
        service.Current.ChatId.Should().BeNull();
    }
}
