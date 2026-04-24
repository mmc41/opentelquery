using System.Text.Json;

namespace OpenTel.Query.Core.Model;

public sealed record LogRecord(
    DateTimeOffset Time,
    long TimeUs,
    string? TraceId,
    string? SpanId,
    string? Level,
    string? Service,
    string? Message,
    IReadOnlyDictionary<string, string> Process,
    IReadOnlyDictionary<string, JsonElement> Attributes);
