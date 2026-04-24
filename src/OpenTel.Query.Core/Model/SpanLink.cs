using System.Text.Json;

namespace OpenTel.Query.Core.Model;

public sealed record SpanLink(
    string TraceId,
    string SpanId,
    IReadOnlyDictionary<string, JsonElement> Attributes);
