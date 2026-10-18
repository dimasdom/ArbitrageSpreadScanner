using Xunit;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArbitrageScanner.Infrastructure.Messaging.Kafka;
using FluentAssertions;

namespace ArbitrageScanner.Tests.Messaging;

public class SpreadKeyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record Vector(
        string Name,
        SpreadKeyType Type,
        string ExchangeA,
        string ExchangeB,
        string Pair,
        string Expected);

    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (var vector in LoadVectors())
        {
            data.Add(vector.Name);
        }

        return data;
    }

    // The same file is carried verbatim by the ArbitrageScanner repo (which owns the producer-side copy of
    // SpreadKey). If this test and its twin ever disagree, keys will land on different partitions.
    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Build_matches_golden_vector(string name)
    {
        var vector = LoadVectors().Single(v => v.Name == name);

        SpreadKey.Build(vector.Type, vector.ExchangeA, vector.ExchangeB, vector.Pair)
            .Should().Be(vector.Expected);
    }

    [Fact]
    public void Build_is_independent_of_exchange_order()
    {
        var forward = SpreadKey.Build(SpreadKeyType.Futures, "Binance", "Kraken", "BTC/USDT");
        var reverse = SpreadKey.Build(SpreadKeyType.Futures, "Kraken", "Binance", "BTC/USDT");

        reverse.Should().Be(forward);
    }

    [Fact]
    public void Build_distinguishes_spread_types_on_the_same_pair()
    {
        var futures = SpreadKey.Build(SpreadKeyType.Futures, "Binance", "Kraken", "BTC/USDT");
        var spot = SpreadKey.Build(SpreadKeyType.Spot, "Binance", "Kraken", "BTC/USDT");

        spot.Should().NotBe(futures);
    }

    [Fact]
    public void Build_never_contains_more_than_the_three_separators()
    {
        var key = SpreadKey.Build(SpreadKeyType.Spot, "a:b", "c:d", "X/Y:Z");

        key.Count(c => c == SpreadKey.Separator).Should().Be(3);
    }

    [Theory]
    [InlineData("", "Kraken", "BTC/USDT")]
    [InlineData("Binance", " ", "BTC/USDT")]
    [InlineData("Binance", "Kraken", "")]
    public void Build_rejects_empty_parts(string exchangeA, string exchangeB, string pair)
    {
        var act = () => SpreadKey.Build(SpreadKeyType.Spot, exchangeA, exchangeB, pair);

        act.Should().Throw<ArgumentException>();
    }

    private static List<Vector> LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Messaging", "spread-key-vectors.json");
        return JsonSerializer.Deserialize<List<Vector>>(File.ReadAllText(path), JsonOptions)!;
    }
}
