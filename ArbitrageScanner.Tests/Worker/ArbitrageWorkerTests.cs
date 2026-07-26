using ArbitrageScanner.Tests.Helpers;
using ArbitrageScanner.Worker.Worker;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Worker;

[Collection(SharedProxyPoolCollection.Name)]
public class ArbitrageWorkerTests
{
    [Fact]
    public async Task StartAsync_ImmediatelyCancelled_CompletesWithoutThrowing()
    {
        var dataService = ServiceFactory.BuildDataService();
        var arbitrageService = ServiceFactory.BuildArbitrageService(dataService: dataService);
        var worker = new ArbitrageWorker(arbitrageService, dataService);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await worker.StartAsync(cts.Token);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_NotCancelled_RunsStartOperationUntilStopped()
    {
        Environment.SetEnvironmentVariable("NODE_TOTAL", null);
        Environment.SetEnvironmentVariable("NODE_INDEX", null);
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        await ServiceFactory.RegisterExchangeService(dataService, config, "worker-test-exchange", "BTC/USDT:USDT");
        var arbitrageService = ServiceFactory.BuildArbitrageService(config, dataService);
        var worker = new ArbitrageWorker(arbitrageService, dataService);

        var act = async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(150);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_StartOperationThrows_LogsErrorAndDoesNotPropagate()
    {
        // No exchange services registered -> GetUniqueCommonFuturesPairsFromApiAsync's
        // pairsPerExchange.First() throws InvalidOperationException, exercising ExecuteAsync's catch block.
        var config = ServiceFactory.BuildConfig();
        var dataService = ServiceFactory.BuildDataService(config);
        var arbitrageService = ServiceFactory.BuildArbitrageService(config, dataService);
        var worker = new ArbitrageWorker(arbitrageService, dataService);

        var act = async () =>
        {
            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(50);
            await worker.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }
}
