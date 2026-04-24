namespace OpenTel.Query.Core.Model;

public sealed record SourceInfo(
    string Tool,
    string Backend,
    string Host,
    IReadOnlyDictionary<string, string?> Properties);
