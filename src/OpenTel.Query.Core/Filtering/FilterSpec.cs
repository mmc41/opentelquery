namespace OpenTel.Query.Core.Filtering;

public enum PatternMode
{
    None = 0,
    Like,
    Regex,
    Glob,
}

public sealed record OperationPattern(string Pattern, PatternMode Mode);

public sealed record AttributeFilter(string Key, string Value);

public sealed record FilterSpec(
    string? Service,
    OperationPattern? Operation,
    string? Status,
    IReadOnlyList<AttributeFilter> Attributes,
    HttpStatusSpec? HttpStatus,
    long? DurationGtUs)
{
    public static readonly FilterSpec Empty = new(
        Service: null,
        Operation: null,
        Status: null,
        Attributes: Array.Empty<AttributeFilter>(),
        HttpStatus: null,
        DurationGtUs: null);

    public bool IsEmpty =>
        Service is null
        && Operation is null
        && Status is null
        && Attributes.Count == 0
        && (HttpStatus is null || HttpStatus.IsEmpty)
        && DurationGtUs is null;
}
