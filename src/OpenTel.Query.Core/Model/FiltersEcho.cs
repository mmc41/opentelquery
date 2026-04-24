namespace OpenTel.Query.Core.Model;

public sealed record FiltersEcho(
    string? Service,
    OperationFilterEcho? Operation,
    string? Status,
    IReadOnlyList<AttributeFilterEcho>? Attributes,
    IReadOnlyList<HttpStatusRangeEcho>? HttpStatus,
    long? DurationGtUs);

public sealed record OperationFilterEcho(string Pattern, string Mode);

public sealed record AttributeFilterEcho(string Key, string Value);

public sealed record HttpStatusRangeEcho(int Low, int High);
