using System.Globalization;

namespace OpenTel.Query.Core.Filtering;

public sealed record HttpStatusSpec(IReadOnlyList<(int Low, int High)> Ranges)
{
    public bool IsEmpty => Ranges.Count == 0;
}

public static class HttpStatusParser
{
    public static HttpStatusSpec Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("HTTP status value cannot be empty.");

        var ranges = new List<(int, int)>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            ranges.Add(ParsePart(part, input));

        return new HttpStatusSpec(ranges);
    }

    private static (int Low, int High) ParsePart(string part, string original)
    {
        var s = part.Trim();

        if (s.Length == 3 && char.IsDigit(s[0]) && (s[1] == 'x' || s[1] == 'X') && (s[2] == 'x' || s[2] == 'X'))
        {
            var firstDigit = s[0] - '0';
            if (firstDigit is < 1 or > 5)
                throw new InvalidOperationException(
                    $"HTTP status class '{s}' must start with digit 1-5. Input was '{original}'.");
            var lo = firstDigit * 100;
            return (lo, lo + 99);
        }

        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            throw new InvalidOperationException(
                $"HTTP status '{s}' is not a valid integer or class (like 5xx). Input was '{original}'.");
        if (code is < 100 or > 599)
            throw new InvalidOperationException(
                $"HTTP status code {code} is outside the valid 100-599 range.");
        return (code, code);
    }
}
