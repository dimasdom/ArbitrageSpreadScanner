using ArbitrageScanner.Tests.Helpers;
using ArbitrageScanner.Worker.Worker;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Worker;

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
}
