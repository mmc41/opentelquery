using OpenTel.Query.Core.Filtering;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Abstractions;

/// <summary>
/// Backend adapter for an OpenTelemetry-compatible observability system
/// (OpenObserve, Jaeger, Tempo, Datadog, ...). Implementations translate
/// generic query specs into backend-specific requests and return the raw
/// JSON response body for the generic assemblers to parse.
/// </summary>
public interface ITelemetryBackend : IDisposable
{
    string BackendName { get; }

    string Host { get; }

    IReadOnlyDictionary<string, string?> Properties { get; }

    string DefaultStreamName { get; }

    Task<string> GetLatestTracesAsync(long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct);

    Task<string> SearchTraceSpansAsync(IReadOnlyCollection<string> traceIds, long startTimeUs, long endTimeUs, CancellationToken ct);

    Task<string> SearchFilteredTraceIdsAsync(FilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct);

    Task<string> SearchLogsAsync(LogsFilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct);

    Task<string> GetAroundAsync(string streamName, string streamType, long keyUs, int size, CancellationToken ct);

    Task<string> ListStreamsAsync(string? streamType, bool fetchSchema, CancellationToken ct);

    Task<string> GetStreamSchemaAsync(string streamName, string streamType, CancellationToken ct);
}
