namespace ArbitrageScanner.Infrastructure.Common
{
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _interval;
        private readonly Queue<DateTime> _requestTimes = new Queue<DateTime>();

        public RateLimiter(int maxRequests, TimeSpan interval)
        {
            _maxRequests = maxRequests;
            _interval = interval;
        }

        public async Task WaitAsync()
        {
            while (true)
            {
                TimeSpan waitTime;
                lock (_requestTimes)
                {
                    var now = DateTime.UtcNow;

                    while (_requestTimes.Count >= _maxRequests)
                    {
                        var oldest = _requestTimes.Peek();
                        if ((now - oldest) < _interval)
                        {
                            break;
                        }

                        _requestTimes.Dequeue();
                    }

                    if (_requestTimes.Count < _maxRequests)
                    {
                        _requestTimes.Enqueue(DateTime.UtcNow);
                        return;
                    }

                    waitTime = _interval - (now - _requestTimes.Peek());
                }

                if (waitTime < TimeSpan.Zero)
                {
                    waitTime = TimeSpan.Zero;
                }

                await Task.Delay(waitTime);
            }
        }
    }
}
