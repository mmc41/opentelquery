using System.Text.Json;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Processing;

public static class LogAssembler
{
    private static readonly string[] MessageFallbackKeys =
    {
        "log", "message", "msg", "content", "data", "json", "body",
    };

    public static IReadOnlyList<LogRecord> Assemble(IEnumerable<JsonElement> hits) =>
        hits.Select(ParseRecord).OrderBy(r => r.TimeUs).ToList();

    private static LogRecord ParseRecord(JsonElement hit)
    {
        string? traceId = null, spanId = null, level = null, service = null, message = null;
        long timeUs = 0;
        var process = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in hit.EnumerateObject())
        {
            var key = property.Name;
            var value = property.Value;

            switch (key)
            {
                case "_timestamp":
                    timeUs = ReadInt64(value);
                    break;
                case "trace_id":
                    traceId = StringOrNull(value);
                    break;
                case "span_id":
                    spanId = StringOrNull(value);
                    break;
                case "level":
                case "log_level":
                case "severity":
                case "severity_text":
                    level ??= StringOrNull(value);
                    break;
                case "service_name":
                    service = StringOrNull(value);
                    process[key] = service ?? "";
                    break;
                default:
                    if (key.StartsWith("service_", StringComparison.Ordinal))
                    {
                        process[key] = value.ValueKind == JsonValueKind.String
                            ? value.GetString() ?? ""
                            : value.GetRawText();
                    }
                    else
                    {
                        attributes[key] = value.Clone();
                    }
                    break;
            }
        }

        foreach (var messageKey in MessageFallbackKeys)
        {
            if (message is not null) break;
            if (attributes.TryGetValue(messageKey, out var value) && value.ValueKind == JsonValueKind.String)
                message = value.GetString();
        }

        return new LogRecord(
            Time: FromUnixMicroseconds(timeUs),
            TimeUs: timeUs,
            TraceId: traceId,
            SpanId: spanId,
            Level: level,
            Service: service,
            Message: message,
            Process: process,
            Attributes: attributes);
    }

    private static string? StringOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long ReadInt64(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetInt64(),
        JsonValueKind.String when long.TryParse(value.GetString(), out var n) => n,
        _ => 0,
    };

    private static DateTimeOffset FromUnixMicroseconds(long us) =>
        DateTimeOffset.FromUnixTimeMilliseconds(us / 1000L);
}
