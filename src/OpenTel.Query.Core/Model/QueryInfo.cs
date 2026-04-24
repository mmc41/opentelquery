namespace OpenTel.Query.Core.Model;

public sealed record QueryInfo(
    string? TraceId,
    int RequestedSize,
    int From,
    int Returned,
    FiltersEcho? Filters = null);
