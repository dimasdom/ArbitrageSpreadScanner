using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class FundingCalculationTests
{
    [Fact]
    public void ARateHigher_ReturnsDiff1_IsALongFalse()
    {
        var svc = ServiceFactory.BuildFundingCalculator();
        var result = svc.CalculateFundingFor(0.001, 0.0005, out bool isALong);

        result.Should().BeApproximately(0.05, 1e-9);
        isALong.Should().BeFalse();
    }

    [Fact]
    public void BRateHigher_ReturnsDiff2_IsALongTrue()
    {
        var svc = ServiceFactory.BuildFundingCalculator();
        var result = svc.CalculateFundingFor(0.0005, 0.001, out bool isALong);

        result.Should().BeApproximately(0.05, 1e-9);
        isALong.Should().BeTrue();
    }

    [Fact]
    public void EqualRates_ReturnsZero_IsALongTrue()
    {
        var svc = ServiceFactory.BuildFundingCalculator();
        var result = svc.CalculateFundingFor(0.001, 0.001, out bool isALong);

        result.Should().Be(0.0);
        isALong.Should().BeTrue();
    }

    [Fact]
    public void BothNegative_ChoosesLargerAbsoluteDifference()
    {
        var svc = ServiceFactory.BuildFundingCalculator();
        var result = svc.CalculateFundingFor(-0.001, -0.003, out bool isALong);

        result.Should().BeApproximately(0.2, 1e-9);
        isALong.Should().BeFalse();
    }

    [Fact]
    public void Result_IsScaledBy100()
    {
        var svc = ServiceFactory.BuildFundingCalculator();
        var result = svc.CalculateFundingFor(0.002, 0.001, out _);

        result.Should().BeApproximately(0.1, 1e-9);
    }
}
