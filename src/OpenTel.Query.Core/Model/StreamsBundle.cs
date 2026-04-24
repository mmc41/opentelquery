namespace OpenTel.Query.Core.Model;

public sealed record StreamsBundle : BundleHeader
{
    public IReadOnlyList<StreamInfo> Streams { get; }

    public const string CurrentSchema = "opentel-query-streams/v1";

    public const string KeyConvention =
        "Lists streams (or the backend's equivalent collection concept). `stream_type` is typically one of "
        + "logs, traces, metrics, enrichment_tables. `stats.doc_time_min_us`/`doc_time_max_us` bound the time "
        + "range with data. When `schema` is null, re-run with --fetch-schema to get the field list.";

    public StreamsBundle(BundleHeader header, IReadOnlyList<StreamInfo> streams)
        : base(header)
    {
        Streams = streams;
    }
}
