namespace OpenTel.Query.Core.Model;

public sealed record SpanException(
    DateTimeOffset Time,
    double TimeOffsetMs,
    string? Type,
    string? Message,
    string? Stacktrace);
