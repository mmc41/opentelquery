using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Processing;

public static class BundleBuilder
{
    public const string ToolName = "OpenTel.Query";

    public static BundleHeader BuildHeader(
        string schema,
        string description,
        string command,
        ITelemetryBackend backend,
        long startTimeUs,
        long endTimeUs,
        int lookbackMinutes,
        QueryInfo queryInfo) =>
        new(
            Schema: schema,
            Description: description,
            Command: command,
            Source: new SourceInfo(
                Tool: ToolName,
                Backend: backend.BackendName,
                Host: backend.Host,
                Properties: backend.Properties),
            Window: new QueryWindow(
                StartTime: DateTimeOffset.FromUnixTimeMilliseconds(startTimeUs / 1000L),
                EndTime: DateTimeOffset.FromUnixTimeMilliseconds(endTimeUs / 1000L),
                StartTimeUs: startTimeUs,
                EndTimeUs: endTimeUs,
                LookbackMinutes: lookbackMinutes),
            QueryInfo: queryInfo);

    public static TraceBundle BuildTraceBundle(
        string command,
        ITelemetryBackend backend,
        long startTimeUs,
        long endTimeUs,
        int lookbackMinutes,
        QueryInfo queryInfo,
        IReadOnlyList<TraceInfo> traces) =>
        new(
            BuildHeader(
                schema: TraceBundle.CurrentSchema,
                description: TraceBundle.KeyConvention,
                command: command,
                backend: backend,
                startTimeUs: startTimeUs,
                endTimeUs: endTimeUs,
                lookbackMinutes: lookbackMinutes,
                queryInfo: queryInfo),
            traces);
}
