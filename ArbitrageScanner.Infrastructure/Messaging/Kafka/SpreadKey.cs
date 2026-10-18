using System.Globalization;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

public enum SpreadKeyType
{
    Futures,
    Funding,
    Spot,
}

/// <summary>
/// Builds the Kafka message key for a spread event: <c>{type}:{exchangeLo}:{exchangeHi}:{pair}</c>.
/// <para>
/// The key is the stable identity of an opportunity, so Open/Update/Close of one spread land on one partition
/// in order. Long/short direction flips with the sign of the spread and therefore must never be part of it:
/// the two exchanges are sorted ordinally instead. Changing this format remaps keys to different partitions —
/// treat any change as breaking. <c>spread-key-vectors.json</c> pins the format in both repositories that
/// carry a copy of this helper.
/// </para>
/// </summary>
public static class SpreadKey
{
    public const char Separator = ':';

    public static string Build(SpreadKeyType type, string exchangeA, string exchangeB, string pair)
    {
        var a = NormalizeExchange(exchangeA, nameof(exchangeA));
        var b = NormalizeExchange(exchangeB, nameof(exchangeB));
        var symbol = NormalizePair(pair);

        var (lo, hi) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

        return string.Create(CultureInfo.InvariantCulture, $"{TypeToken(type)}{Separator}{lo}{Separator}{hi}{Separator}{symbol}");
    }

    public static string TypeToken(SpreadKeyType type) => type switch
    {
        SpreadKeyType.Futures => "futures",
        SpreadKeyType.Funding => "funding",
        SpreadKeyType.Spot => "spot",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown spread type."),
    };

    /// <summary>Lower-cases, collapses runs of whitespace to a single '-', and replaces the key separator.</summary>
    internal static string NormalizeExchange(string exchange, string paramName)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            throw new ArgumentException("Exchange name must not be empty.", paramName);
        }

        var words = exchange.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('-', words).ToLowerInvariant().Replace(Separator, '.');
    }

    /// <summary>Upper-cases and maps ccxt-style <c>BTC/USDT:USDT</c> to <c>BTC-USDT.USDT</c> so the pair never
    /// contains the key separator and a settle-currency suffix stays visible.</summary>
    internal static string NormalizePair(string pair)
    {
        if (string.IsNullOrWhiteSpace(pair))
        {
            throw new ArgumentException("Pair must not be empty.", nameof(pair));
        }

        return pair.Trim().ToUpperInvariant().Replace('/', '-').Replace(Separator, '.');
    }
}
