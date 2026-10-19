using ArbitrageScanner.Futures.Services;
using ArbitrageScanner.Spot.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests;

public class SpreadCalculationTests
{
    [Fact]
    public void Futures_PositiveSpread_ReturnsCorrectPercent()
    {
        FuturesPositionCalculatorService.CalculateSpreadFor(100.0, 95.0).Should().BeApproximately(5.2631578947, 1e-6);
    }

    [Fact]
    public void Futures_NegativeSpread_ReturnsNegativePercent()
    {
        FuturesPositionCalculatorService.CalculateSpreadFor(95.0, 100.0).Should().BeApproximately(-5.0, 1e-6);
    }

    [Fact]
    public void Futures_EqualPrices_ReturnsZero()
    {
        FuturesPositionCalculatorService.CalculateSpreadFor(100.0, 100.0).Should().Be(0.0);
    }

    [Fact]
    public void Futures_SmallDeltaHighBase_ReturnsSmallPercent()
    {
        FuturesPositionCalculatorService.CalculateSpreadFor(50001.0, 50000.0).Should().BeApproximately(0.002, 1e-6);
    }

    [Fact]
    public void Spot_PositiveSpread_ReturnsCorrectPercent()
    {
        SpotPositionCalculatorService.CalculateSpreadFor(100.0, 95.0).Should().BeApproximately(5.2631578947, 1e-6);
    }

    [Fact]
    public void Spot_NegativeSpread_ReturnsNegativePercent()
    {
        SpotPositionCalculatorService.CalculateSpreadFor(95.0, 100.0).Should().BeApproximately(-5.0, 1e-6);
    }

    [Fact]
    public void Basis_MarkAboveIndex_ReturnsPositive()
    {
        SpotPositionCalculatorService.CalculateBasis(101.0, 100.0).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Basis_MarkBelowIndex_ReturnsNegative()
    {
        SpotPositionCalculatorService.CalculateBasis(99.0, 100.0).Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public void Basis_EqualPrices_ReturnsZero()
    {
        SpotPositionCalculatorService.CalculateBasis(100.0, 100.0).Should().Be(0.0);
    }
}
