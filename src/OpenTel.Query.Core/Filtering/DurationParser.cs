using System.Globalization;

namespace OpenTel.Query.Core.Filtering;

public static class DurationParser
{
    public static long ParseToMicroseconds(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("Duration value cannot be empty.");

        var trimmed = input.Trim();

        (string number, long multiplierUs) = trimmed switch
        {
            _ when trimmed.EndsWith("us", StringComparison.OrdinalIgnoreCase) => (trimmed[..^2], 1L),
            _ when trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => (trimmed[..^2], 1_000L),
            _ when trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase) => (trimmed[..^1], 1_000_000L),
            _ when trimmed.EndsWith("m", StringComparison.OrdinalIgnoreCase) => (trimmed[..^1], 60L * 1_000_000L),
            _ when trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase) => (trimmed[..^1], 3_600L * 1_000_000L),
            _ => throw new InvalidOperationException(
                $"Duration '{input}' is not valid. Use one of: 500us, 250ms, 2s, 3m, 1h."),
        };

        var digits = number.Trim();
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException(
                $"Duration '{input}' is not valid. Numeric part '{digits}' could not be parsed.");
        if (value < 0)
            throw new InvalidOperationException(
                $"Duration '{input}' must not be negative.");

        var us = (long)Math.Round(value * multiplierUs, MidpointRounding.AwayFromZero);
        return us;
    }
}
