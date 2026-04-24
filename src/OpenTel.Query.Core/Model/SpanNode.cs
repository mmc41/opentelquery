using System.Text.Json;

namespace OpenTel.Query.Core.Model;

public sealed record SpanNode(
    string SpanId,
    string? ParentSpanId,
    string Operation,
    string Service,
    string Kind,
    int KindCode,
    string Status,
    DateTimeOffset StartTime,
    double StartOffsetMs,
    double DurationMs,
    DateTimeOffset EndTime,
    int Flags,
    IReadOnlyDictionary<string, string> Process,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<SpanEvent> Events,
    IReadOnlyList<SpanException> Exceptions,
    IReadOnlyList<SpanLink> Links,
    IReadOnlyList<SpanNode> Children);
