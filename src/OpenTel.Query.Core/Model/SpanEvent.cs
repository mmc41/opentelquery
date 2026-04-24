using System.Text.Json;

namespace OpenTel.Query.Core.Model;

public sealed record SpanEvent(
    DateTimeOffset Time,
    double TimeOffsetMs,
    string Name,
    IReadOnlyDictionary<string, JsonElement> Attributes);
