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

        /// <summary>
        /// Delays for <paramref name="delay"/>, swallowing the cancellation exception so a caller's
        /// `while (!cancellationToken.IsCancellationRequested)` loop can exit on the next check instead
        /// of propagating an OperationCanceledException out of a retry/backoff delay.
        /// </summary>
        public static async Task DelayRetry(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown — caller's loop condition handles the exit.
            }
        }
    }
}
