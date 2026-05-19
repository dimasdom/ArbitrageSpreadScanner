using ArbitrageScanner.Infrastructure.Services;

namespace ArbitrageScanner.Infrastructure.Common
{
    public static class TaskExtensions
    {
        public static void FireAndForgetWithLogging(this Task task, DataService dataService, string method, string symbol = "", string exchange = "")
        {
            task.ContinueWith(
                t => dataService.LogErrorEntry(t.Exception!.GetBaseException(), symbol, method, exchange),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
