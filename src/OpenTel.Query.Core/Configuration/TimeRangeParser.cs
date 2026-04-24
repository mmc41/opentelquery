using System.Globalization;

namespace OpenTel.Query.Core.Configuration;

public static class TimeRangeParser
{
    public static (long StartUs, long EndUs) Resolve(
        string? since,
        string? until,
        TimeProvider time,
        int defaultLookbackMinutes)
    {
        var now = time.GetUtcNow();
        long endUs;
        long startUs;

        if (until is not null)
            endUs = ParseToMicroseconds(until, now, "--until");
        else
            endUs = ToUnixMicroseconds(now);

        if (since is not null)
            startUs = ParseToMicroseconds(since, now, "--since");
        else
            startUs = endUs - (long)defaultLookbackMinutes * 60 * 1_000_000L;

        if (startUs >= endUs)
            throw new InvalidOperationException(
                $"--since must resolve to a moment strictly before --until. Got start={startUs}us, end={endUs}us.");

        return (startUs, endUs);
    }

    public static long ParseToMicroseconds(string input, DateTimeOffset now, string optionName)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException($"{optionName} value cannot be empty.");

        var trimmed = input.Trim();

        if (TryParseRelative(trimmed, now, out var relative))
            return relative;

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var absolute))
            return ToUnixMicroseconds(absolute);

        throw new InvalidOperationException(
            $"{optionName} value '{input}' is not a valid time. "
            + "Use ISO-8601 (e.g. 2026-04-20T14:00:00Z) or a relative value like '15m ago', '2h ago', '3d ago'.");
    }

    private static bool TryParseRelative(string input, DateTimeOffset now, out long us)
    {
        us = 0;

        const string suffix = "ago";
        if (!input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var head = input[..^suffix.Length].TrimEnd();
        if (head.Length < 2)
            return false;

        var unit = head[^1];
        var numberPart = head[..^1].TrimEnd();

        if (!long.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            return false;
        if (amount < 0)
            return false;

        long seconds = unit switch
        {
            'd' or 'D' => amount * 86400L,
            'h' or 'H' => amount * 3600L,
            'm' or 'M' => amount * 60L,
            's' or 'S' => amount,
            _ => -1L,
        };
        if (seconds < 0)
            return false;

        us = ToUnixMicroseconds(now) - seconds * 1_000_000L;
        return true;
    }

    public static long ToUnixMicroseconds(DateTimeOffset moment) =>
        moment.ToUnixTimeMilliseconds() * 1000L;
}
