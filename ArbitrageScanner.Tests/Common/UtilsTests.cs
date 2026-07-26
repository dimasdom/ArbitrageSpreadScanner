using ArbitrageScanner.Infrastructure.Common;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Common;

public class UtilsTests
{
    [Fact]
    public void Shuffle_PreservesAllElements()
    {
        var list = Enumerable.Range(1, 20).ToList();

        list.Shuffle();

        list.Should().BeEquivalentTo(Enumerable.Range(1, 20));
    }

    [Fact]
    public void Shuffle_EmptyList_DoesNotThrow()
    {
        var list = new List<int>();

        var act = () => list.Shuffle();

        act.Should().NotThrow();
    }

    [Fact]
    public void Shuffle_SingleElement_DoesNotThrow()
    {
        var list = new List<int> { 42 };

        list.Shuffle();

        list.Should().ContainSingle().Which.Should().Be(42);
    }
}
