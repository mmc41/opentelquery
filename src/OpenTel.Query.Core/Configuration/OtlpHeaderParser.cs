namespace OpenTel.Query.Core.Configuration;

public static class OtlpHeaderParser
{
    public static IReadOnlyDictionary<string, string> Parse(string headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in headers.Split(','))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;
            var idx = trimmed.IndexOf('=');
            if (idx <= 0) continue;
            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim();
            result[key] = value;
        }
        return result;
    }
}
