using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class SpreadCalculationTests
{
    [Fact]
    public void Futures_PositiveSpread_ReturnsCorrectPercent()
    {
        var svc = ServiceFactory.BuildFuturesCalculator();
        svc.CalculateSpreadFor(100.0, 95.0).Should().BeApproximately(5.2631578947, 1e-6);
    }

    [Fact]
    public void Futures_NegativeSpread_ReturnsNegativePercent()
    {
        var svc = ServiceFactory.BuildFuturesCalculator();
        svc.CalculateSpreadFor(95.0, 100.0).Should().BeApproximately(-5.0, 1e-6);
    }

    [Fact]
    public void Futures_EqualPrices_ReturnsZero()
    {
        var svc = ServiceFactory.BuildFuturesCalculator();
        svc.CalculateSpreadFor(100.0, 100.0).Should().Be(0.0);
    }

    [Fact]
    public void Futures_SmallDeltaHighBase_ReturnsSmallPercent()
    {
        var svc = ServiceFactory.BuildFuturesCalculator();
        svc.CalculateSpreadFor(50001.0, 50000.0).Should().BeApproximately(0.002, 1e-6);
    }

    [Fact]
    public void Spot_PositiveSpread_ReturnsCorrectPercent()
    {
        var svc = ServiceFactory.BuildSpotCalculator();
        svc.CalculateSpreadFor(100.0, 95.0).Should().BeApproximately(5.2631578947, 1e-6);
    }

    [Fact]
    public void Spot_NegativeSpread_ReturnsNegativePercent()
    {
        var svc = ServiceFactory.BuildSpotCalculator();
        svc.CalculateSpreadFor(95.0, 100.0).Should().BeApproximately(-5.0, 1e-6);
    }

    [Fact]
    public void Basis_MarkAboveIndex_ReturnsPositive()
    {
        var svc = ServiceFactory.BuildSpotCalculator();
        svc.CalculateBasis(101.0, 100.0).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Basis_MarkBelowIndex_ReturnsNegative()
    {
        var svc = ServiceFactory.BuildSpotCalculator();
        svc.CalculateBasis(99.0, 100.0).Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public void Basis_EqualPrices_ReturnsZero()
    {
        var svc = ServiceFactory.BuildSpotCalculator();
        svc.CalculateBasis(100.0, 100.0).Should().Be(0.0);
    }
}
