using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class UserInterfaceServiceTests
{
    private static ExchangeRateModel Rate(string symbol, string exchange, double rate = 100, double? fundingRate = null) => new()
    {
        Symbol = symbol,
        Exchange = exchange,
        ExchangeRate = rate,
        FundingRate = fundingRate.HasValue ? new ccxt.FundingRate { fundingRate = fundingRate } : null,
    };

    private static (UserInterfaceService service, Mock<ITelegramNotifierService> telegram) BuildService()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        return (service, telegram);
    }

    [Fact]
    public void ShowPositionInfoInConsole_ValidOpportunity_DoesNotThrow()
    {
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            Spread = 1.5,
        };

        var act = () => UserInterfaceService.ShowPositionInfoInConsole(opportunity);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShowPositionInfoInConsole_MissingExchangeRates_DoesNothing()
    {
        var opportunity = new TradeOpportunityModel();

        var act = () => UserInterfaceService.ShowPositionInfoInConsole(opportunity);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SaveJsonToFile_WritesJsonToDisk()
    {
        var (service, _) = BuildService();
        var path = Path.Combine(Path.GetTempPath(), $"arbi-test-{Guid.NewGuid()}.json");
        try
        {
            await service.SaveJsonToFile(new { A = 1, B = "x" }, path);

            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("\"A\":1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveJsonToFile_InvalidPath_DoesNotThrow()
    {
        var (service, _) = BuildService();

        var act = async () => await service.SaveJsonToFile(new { A = 1 }, "/nonexistent-dir-xyz/file.json");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetDataFromJsonFile_RoundTripsData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arbi-test-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, "{\"Symbol\":\"BTC/USDT\"}");
        try
        {
            var result = UserInterfaceService.GetDataFromJsonFile<Dictionary<string, string>>(path);

            result.Should().ContainKey("Symbol").WhoseValue.Should().Be("BTC/USDT");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetDataFromJsonFile_MissingFile_ReturnsDefault()
    {
        var result = UserInterfaceService.GetDataFromJsonFile<Dictionary<string, string>>("/nonexistent-xyz.json");

        result.Should().BeNull();
    }

    [Fact]
    public async Task PostFoundSpreadToTelegram_SendsFormattedMessageWithFunding()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance", fundingRate: 0.001),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx", fundingRate: -0.001),
            Spread = 1.23,
        };

        await service.PostFoundSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s =>
            s.Contains("BTC/USDT:USDT") && s.Contains("Funding binance") && s.Contains("Funding okx"))), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpreadToTelegram_NoFundingData_SendsMessageWithoutFundingLines()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance"),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx"),
            Spread = 1.23,
        };

        await service.PostFoundSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpreadToTelegram_TelegramThrows_LogsAndDoesNotPropagate()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance"),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx"),
        };

        var act = async () => await service.PostFoundSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PostFoundSpreadToTelegram_AllFieldsNull_SendsMessageWithoutThrowing()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel();

        var act = async () => await service.PostFoundSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpreadToTelegram_OnlyLongHasFunding_IncludesOnlyLongFundingLine()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance", fundingRate: 0.001),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx"),
            Spread = 1.23,
        };

        await service.PostFoundSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s =>
            s.Contains("Funding binance") && !s.Contains("Funding okx"))), Times.Once);
    }

    [Fact]
    public async Task PostFoundFundingSpreadToTelegram_AllFieldsNull_SendsMessageWithoutThrowing()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel();

        var act = async () => await service.PostFoundFundingSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PostFoundFundingSpreadToTelegram_OnlyShortHasFunding_IncludesOnlyShortFundingLine()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance"),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx", fundingRate: -0.001),
            TotalFunding = 0.5,
            PossibleProfit = 0.2,
        };

        await service.PostFoundFundingSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s =>
            s.Contains("Funding okx") && !s.Contains("Funding binance"))), Times.Once);
    }

    [Fact]
    public async Task PostFoundFundingSpreadToTelegram_TelegramThrows_LogsAndDoesNotPropagate()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        var opportunity = new TradeOpportunityModel();

        var act = async () => await service.PostFoundFundingSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PostFoundSpotSpreadToTelegram_AllFieldsNull_SendsMessageWithoutThrowing()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel();

        var act = async () => await service.PostFoundSpotSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpotSpreadToTelegram_NoFundingData_OmitsFundingLine()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT", "binance"),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx"),
            Spread = 0.8,
            PossibleProfit = 0.3,
        };

        await service.PostFoundSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => !s.Contains("\nFunding:"))), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpotSpreadToTelegram_TelegramThrows_LogsAndDoesNotPropagate()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        var opportunity = new TradeOpportunityModel();

        var act = async () => await service.PostFoundSpotSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PostFoundFundingSpreadToTelegram_SendsFormattedMessage()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT:USDT", "binance", fundingRate: 0.001),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx", fundingRate: -0.001),
            TotalFunding = 0.5,
            PossibleProfit = 0.2,
        };

        await service.PostFoundFundingSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Funding Spread"))), Times.Once);
    }

    [Fact]
    public async Task PostFoundSpotSpreadToTelegram_SendsFormattedMessage()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT", "binance"),
            ExchangeShort = Rate("BTC/USDT:USDT", "okx", fundingRate: 0.0005),
            Spread = 0.8,
            PossibleProfit = 0.3,
        };

        await service.PostFoundSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spot Spread"))), Times.Once);
    }

    [Fact]
    public async Task PostInvalidatingSpreadToTelegram_ValidOpportunity_SendsMessage()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
        };

        await service.PostInvalidatingSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spread closed"))), Times.Once);
    }

    [Fact]
    public async Task PostInvalidatingSpreadToTelegram_MissingRates_DoesNotSend()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel();

        await service.PostInvalidatingSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostInvalidatingSpreadToTelegram_OnlyExchangeRateBMissing_DoesNotSend()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel { ExchangeRateA = Rate("BTC/USDT:USDT", "binance") };

        await service.PostInvalidatingSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostInvalidatingSpotSpreadToTelegram_ValidOpportunity_SendsMessage()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
            ExchangeLong = Rate("BTC/USDT", "binance"),
        };

        await service.PostInvalidatingSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spot Spread closed"))), Times.Once);
    }

    [Fact]
    public async Task PostInvalidatingSpotSpreadToTelegram_MissingRates_DoesNotSend()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel();

        await service.PostInvalidatingSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostInvalidatingSpotSpreadToTelegram_OnlyExchangeLongMissing_DoesNotSend()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel { ExchangeRateA = Rate("BTC/USDT", "binance") };

        await service.PostInvalidatingSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostInvalidatingSpotSpreadToTelegram_MissingExchangeRateB_StillSendsWithNullPlaceholder()
    {
        var (service, telegram) = BuildService();
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT", "binance"),
            ExchangeLong = Rate("BTC/USDT", "binance"),
        };

        await service.PostInvalidatingSpotSpreadToTelegram(opportunity);

        telegram.Verify(t => t.SendMessageAsync(It.Is<string>(s => s.Contains("Spot Spread closed"))), Times.Once);
    }

    [Fact]
    public async Task PostInvalidatingSpotSpreadToTelegram_TelegramThrows_LogsAndDoesNotPropagate()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT", "binance"),
            ExchangeLong = Rate("BTC/USDT", "binance"),
        };

        var act = async () => await service.PostInvalidatingSpotSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PostInvalidatingSpreadToTelegram_TelegramThrows_LogsAndDoesNotPropagate()
    {
        var telegram = new Mock<ITelegramNotifierService>();
        telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dataService = ServiceFactory.BuildDataService();
        var service = new UserInterfaceService(ServiceFactory.BuildConfig(), dataService, telegram.Object);
        var opportunity = new TradeOpportunityModel
        {
            ExchangeRateA = Rate("BTC/USDT:USDT", "binance"),
            ExchangeRateB = Rate("BTC/USDT:USDT", "okx"),
        };

        var act = async () => await service.PostInvalidatingSpreadToTelegram(opportunity);

        await act.Should().NotThrowAsync();
    }
}
