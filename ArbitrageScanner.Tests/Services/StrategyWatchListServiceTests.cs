using ArbitrageScanner.Domain.Models;
using ArbitrageScanner.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

public class StrategyWatchListServiceTests
{
    private static TradeOpportunityModel Opportunity() => new() { Guid = Guid.NewGuid() };

    [Fact]
    public void TryAdd_NewKey_AddsAndReturnsTrue()
    {
        var service = new StrategyWatchListService();
        var key = Guid.NewGuid().ToString();

        var result = service.TryAdd(key, Opportunity());

        result.Should().BeTrue();
        service.ContainsKey(key).Should().BeTrue();
    }

    [Fact]
    public void TryAdd_ExistingKey_ReturnsFalse()
    {
        var service = new StrategyWatchListService();
        var key = Guid.NewGuid().ToString();
        service.TryAdd(key, Opportunity());

        var result = service.TryAdd(key, Opportunity());

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsKey_MissingKey_ReturnsFalse()
    {
        var service = new StrategyWatchListService();

        service.ContainsKey(Guid.NewGuid().ToString()).Should().BeFalse();
    }

    [Fact]
    public void Set_NewKey_AddsItem()
    {
        var service = new StrategyWatchListService();
        var key = Guid.NewGuid().ToString();
        var opportunity = Opportunity();

        service.Set(key, opportunity);

        service.Items[key].Should().BeSameAs(opportunity);
    }

    [Fact]
    public void Set_ExistingKey_OverwritesItem()
    {
        var service = new StrategyWatchListService();
        var key = Guid.NewGuid().ToString();
        service.Set(key, Opportunity());
        var replacement = Opportunity();

        service.Set(key, replacement);

        service.Items[key].Should().BeSameAs(replacement);
    }

    [Fact]
    public void TryRemove_ExistingKey_RemovesAndReturnsValue()
    {
        var service = new StrategyWatchListService();
        var key = Guid.NewGuid().ToString();
        var opportunity = Opportunity();
        service.Set(key, opportunity);

        var removed = service.TryRemove(key, out var value);

        removed.Should().BeTrue();
        value.Should().BeSameAs(opportunity);
        service.ContainsKey(key).Should().BeFalse();
    }

    [Fact]
    public void TryRemove_MissingKey_ReturnsFalse()
    {
        var service = new StrategyWatchListService();

        var removed = service.TryRemove(Guid.NewGuid().ToString(), out var value);

        removed.Should().BeFalse();
        value.Should().BeNull();
    }
}
