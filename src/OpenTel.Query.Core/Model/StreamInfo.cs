namespace OpenTel.Query.Core.Model;

public sealed record StreamInfo(
    string Name,
    string StreamType,
    string? StorageType,
    StreamStats? Stats,
    StreamSettings? Settings,
    IReadOnlyList<FieldInfo>? Schema);

public sealed record StreamStats(
    long? DocTimeMinUs,
    long? DocTimeMaxUs,
    long? DocNum,
    long? FileNum,
    double? StorageSize,
    double? CompressedSize);

public sealed record StreamSettings(
    IReadOnlyList<string> PartitionKeys,
    IReadOnlyList<string> FullTextSearchKeys);
