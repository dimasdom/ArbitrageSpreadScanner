using ArbitrageScanner.Infrastructure.Common;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Common;

public class RateLimiterTests
{
    [Fact]
    public async Task WaitAsync_UnderLimit_ReturnsImmediately()
    {
        var limiter = new RateLimiter(5, TimeSpan.FromSeconds(10));

        var task = limiter.WaitAsync();
        var completed = await Task.WhenAny(task, Task.Delay(500));

        completed.Should().Be(task);
    }

    [Fact]
    public async Task WaitAsync_AtLimit_DelaysUntilOldestRequestExpires()
    {
        var limiter = new RateLimiter(1, TimeSpan.FromMilliseconds(200));

        await limiter.WaitAsync();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(150);
    }

    [Fact]
    public async Task WaitAsync_MultipleCallsWithinLimit_AllComplete()
    {
        var limiter = new RateLimiter(3, TimeSpan.FromSeconds(5));

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        var task = limiter.WaitAsync();
        var completed = await Task.WhenAny(task, Task.Delay(50));
        completed.Should().NotBe(task);
    }
}
