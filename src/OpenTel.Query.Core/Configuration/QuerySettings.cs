using Microsoft.Extensions.Configuration;

namespace OpenTel.Query.Core.Configuration;

public sealed record QuerySettings(int LookbackMinutes)
{
    public static QuerySettings Load(IConfiguration cfg)
    {
        var raw = cfg["Query:LookbackMinutes"]
            ?? throw new InvalidOperationException(
                "Query:LookbackMinutes is not configured. Set it in appsettings.json.");
        if (!int.TryParse(raw, out var lookbackMinutes) || lookbackMinutes <= 0)
            throw new InvalidOperationException(
                $"Query:LookbackMinutes must be a positive integer. Got: '{raw}'.");
        return new QuerySettings(lookbackMinutes);
    }
}
