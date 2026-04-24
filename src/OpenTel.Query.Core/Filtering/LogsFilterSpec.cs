namespace OpenTel.Query.Core.Filtering;

public sealed record LogsFilterSpec(
    string? TraceId,
    string? Service,
    string? Level,
    string? Match,
    string? MatchField,
    OperationPattern? FieldMatch)
{
    public static readonly LogsFilterSpec Empty = new(
        TraceId: null,
        Service: null,
        Level: null,
        Match: null,
        MatchField: null,
        FieldMatch: null);

    public bool IsEmpty =>
        TraceId is null
        && Service is null
        && Level is null
        && Match is null
        && MatchField is null
        && FieldMatch is null;
}
