namespace OpenTel.Query.Core.Model;

public sealed record LogBundle : BundleHeader
{
    public IReadOnlyList<LogRecord> Logs { get; }

    public const string CurrentSchema = "opentel-query-log/v1";

    public const string KeyConvention =
        "Log records from the selected telemetry backend. `message` is best-effort from common body fields "
        + "(log, message, msg, content, data, json, body). Resource attributes (keys starting with `service_`) land "
        + "under `process`; other columns land under `attributes`. `trace_id`/`span_id` are filled when the "
        + "log was emitted in the scope of an OpenTelemetry span.";

    public LogBundle(BundleHeader header, IReadOnlyList<LogRecord> logs)
        : base(header)
    {
        Logs = logs;
    }
}
