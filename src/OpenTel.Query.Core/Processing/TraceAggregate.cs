using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Processing;

public sealed record TraceAggregate(
    string? RootOperation,
    string? RootService,
    IReadOnlyList<ServiceCount>? Services);
