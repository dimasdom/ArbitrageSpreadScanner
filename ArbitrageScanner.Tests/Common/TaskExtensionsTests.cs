using ArbitrageScanner.Domain.Interfaces;
using ArbitrageScanner.Infrastructure.Common;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace ArbitrageScanner.Tests.Common;

public class TaskExtensionsTests
{
    [Fact]
    public async Task FireAndForgetWithLogging_FaultedTask_LogsError()
    {
        var mockRepo = new Mock<ITradeOpportunityRepository>();
        mockRepo.Setup(r => r.SaveError(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));
        var dataService = new DataService(mockRepo.Object, ServiceFactory.BuildConfig());
        var tcs = new TaskCompletionSource();

        tcs.Task.FireAndForgetWithLogging(dataService, "TestMethod", "BTC/USDT", "binance");
        tcs.SetException(new InvalidOperationException("boom"));
        await Task.Delay(50);

        mockRepo.Verify(r => r.SaveError(
            It.Is<Exception>(e => e.Message == "boom"), "BTC/USDT", "TestMethod", "binance"), Times.Once);
    }

    [Fact]
    public async Task FireAndForgetWithLogging_SuccessfulTask_DoesNotInvokeContinuation()
    {
        var dataService = ServiceFactory.BuildDataService();
        var task = Task.CompletedTask;

        var act = () => task.FireAndForgetWithLogging(dataService, "TestMethod");

        act.Should().NotThrow();
        await Task.Delay(20);
    }

    [Fact]
    public async Task DelayRetry_NotCancelled_CompletesAfterDelay()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(30);
    }

    [Fact]
    public async Task DelayRetry_CancelledBeforeDelayElapses_ReturnsWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await ArbitrageScanner.Infrastructure.Common.TaskExtensions.DelayRetry(TimeSpan.FromSeconds(5), cts.Token);

        await act.Should().NotThrowAsync();
    }
}
