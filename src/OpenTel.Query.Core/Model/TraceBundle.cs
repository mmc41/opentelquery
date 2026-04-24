namespace OpenTel.Query.Core.Model;

public sealed record TraceBundle : BundleHeader
{
    public IReadOnlyList<TraceInfo> Traces { get; }

    public const string CurrentSchema = "opentel-query-trace/v1";

    public const string KeyConvention =
        "Attribute keys in `process` and `attributes` are OpenTelemetry semantic-convention names "
        + "normalized to the backend's column form (dots replaced by underscores for SQL-backed backends). "
        + "E.g. `service_name` means OTel `service.name`; `http_request_method` means OTel `http.request.method`. "
        + "Resource attributes (OTel resource) land under `process`; span attributes land under `attributes`.";

    public TraceBundle(BundleHeader header, IReadOnlyList<TraceInfo> traces)
        : base(header)
    {
        Traces = traces;
    }
}
