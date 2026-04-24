namespace OpenTel.Query.Core.Model;

public sealed record TraceInfo(
    string TraceId,
    string RootOperation,
    string RootService,
    int SpanCount,
    int ErrorCount,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    double DurationMs,
    IReadOnlyList<ServiceCount> Services,
    IReadOnlyList<SpanNode> RootSpans);
