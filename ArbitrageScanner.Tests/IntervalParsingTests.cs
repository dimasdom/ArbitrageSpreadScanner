using ArbitrageScanner.Funding.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class IntervalParsingTests
{
    [Fact]
    public void ParseInterval_HoursOnly_ReturnsCorrectTimeSpan()
    {
        FundingObserverService.ParseInterval("8h").Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void ParseInterval_MinutesOnly_ReturnsCorrectTimeSpan()
    {
        FundingObserverService.ParseInterval("45m").Should().Be(TimeSpan.FromMinutes(45));
    }

    [Fact]
    public void ParseInterval_HoursAndMinutes_ReturnsCorrectTimeSpan()
    {
        FundingObserverService.ParseInterval("1h30m").Should().Be(new TimeSpan(1, 30, 0));
    }

    [Fact]
    public void ParseInterval_MixedCase_IsCaseInsensitive()
    {
        FundingObserverService.ParseInterval("4H").Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public void GetNextPayoutUtc_EmptyString_ReturnsDefault()
    {
        var svc = ServiceFactory.BuildFundingObserver();
        var result = svc.GetNextPayoutUtc("");
        result.Should().Be(default(DateTime));
    }

    [Fact]
    public void GetNextPayoutUtc_WhitespaceString_ReturnsDefault()
    {
        var svc = ServiceFactory.BuildFundingObserver();
        var result = svc.GetNextPayoutUtc("   ");
        result.Should().Be(default(DateTime));
    }

    [Fact]
    public void GetNextPayoutUtc_ValidInterval_ReturnsFutureBoundary()
    {
        var svc = ServiceFactory.BuildFundingObserver();
        var before = DateTime.UtcNow;
        var result = svc.GetNextPayoutUtc("8h");
        var after = DateTime.UtcNow;

        result.Should().BeAfter(before);
        var intervalTicks = TimeSpan.FromHours(8).Ticks;
        (result.Ticks % intervalTicks).Should().Be(0);
    }

    [Fact]
    public void GetNextPayoutUtc_1Hour_IsWithin1HourOfNow()
    {
        var svc = ServiceFactory.BuildFundingObserver();
        var result = svc.GetNextPayoutUtc("1h");
        var limit = DateTime.UtcNow.AddHours(1);
        result.Should().BeBefore(limit.AddSeconds(1));
    }
}
