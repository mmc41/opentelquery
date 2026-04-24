namespace OpenTel.Query.Core.Model;

public sealed record SchemaBundle : BundleHeader
{
    public string Stream { get; }
    public string StreamType { get; }
    public IReadOnlyList<FieldInfo> Fields { get; }
    public StreamSettings? Settings { get; }

    public const string CurrentSchema = "opentel-query-schema/v1";

    public const string KeyConvention =
        "Describes the schema of a single stream. `fields[].type` is backend-dependent; common values include "
        + "Utf8, Int64, Float64, Timestamp, Boolean. Use this list to write accurate filters.";

    public SchemaBundle(BundleHeader header, string stream, string streamType, IReadOnlyList<FieldInfo> fields, StreamSettings? settings)
        : base(header)
    {
        Stream = stream;
        StreamType = streamType;
        Fields = fields;
        Settings = settings;
    }
}
