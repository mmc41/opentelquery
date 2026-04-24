namespace OpenTel.Query.Core.Model;

public sealed record QueryWindow(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    long StartTimeUs,
    long EndTimeUs,
    int LookbackMinutes);
