using OpenTel.Query.Core.Configuration;

namespace OpenTel.Query.Core.UnitTests;

public class TimeRangeParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NowUs = Now.ToUnixTimeMilliseconds() * 1000L;

    private static FakeTimeProvider Time() => new(Now);

    [Fact]
    public void Resolve_NeitherSinceNorUntil_UsesLookbackMinutesWindowEndingNow()
    {
        var (startUs, endUs) = TimeRangeParser.Resolve(since: null, until: null, Time(), defaultLookbackMinutes: 15);

        Assert.Equal(NowUs, endUs);
        Assert.Equal(NowUs - 15L * 60 * 1_000_000L, startUs);
    }

    [Fact]
    public void Resolve_RelativeSince_SubtractsFromNow()
    {
        var (startUs, endUs) = TimeRangeParser.Resolve(since: "2h ago", until: null, Time(), defaultLookbackMinutes: 400);

        Assert.Equal(NowUs, endUs);
        Assert.Equal(NowUs - 2L * 3600 * 1_000_000L, startUs);
    }

    [Fact]
    public void Resolve_RelativeSinceMinutes_IsSubtractedCorrectly()
    {
        var (startUs, _) = TimeRangeParser.Resolve(since: "30m ago", until: null, Time(), defaultLookbackMinutes: 400);

        Assert.Equal(NowUs - 30L * 60 * 1_000_000L, startUs);
    }

    [Fact]
    public void Resolve_RelativeSinceDays_IsSubtractedCorrectly()
    {
        var (startUs, _) = TimeRangeParser.Resolve(since: "3d ago", until: null, Time(), defaultLookbackMinutes: 400);

        Assert.Equal(NowUs - 3L * 86400 * 1_000_000L, startUs);
    }

    [Fact]
    public void Resolve_AbsoluteUntil_IsParsedAsUtc()
    {
        var (_, endUs) = TimeRangeParser.Resolve(
            since: "10d ago",
            until: "2026-04-20T14:00:00Z",
            Time(),
            defaultLookbackMinutes: 400);

        var expectedUs = new DateTimeOffset(2026, 04, 20, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;
        Assert.Equal(expectedUs, endUs);
    }

    [Fact]
    public void Resolve_AbsoluteSinceAndUntil_BothParsed()
    {
        var (startUs, endUs) = TimeRangeParser.Resolve(
            since: "2026-04-20T13:00:00Z",
            until: "2026-04-20T14:00:00Z",
            Time(),
            defaultLookbackMinutes: 400);

        Assert.Equal(new DateTimeOffset(2026, 04, 20, 13, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L, startUs);
        Assert.Equal(new DateTimeOffset(2026, 04, 20, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L, endUs);
    }

    [Fact]
    public void Resolve_StartNotBeforeEnd_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(
                since: "2026-04-20T15:00:00Z",
                until: "2026-04-20T14:00:00Z",
                Time(),
                defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_EmptySinceValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(since: "   ", until: null, Time(), defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_InvalidSinceSuffix_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(since: "5x ago", until: null, Time(), defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_InvalidAbsoluteValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(since: "not-a-date", until: null, Time(), defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_NegativeRelative_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(since: "-5m ago", until: null, Time(), defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_RelativeWithoutAgoSuffix_TreatedAsAbsoluteAndFails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TimeRangeParser.Resolve(since: "5m", until: null, Time(), defaultLookbackMinutes: 400));
    }

    [Fact]
    public void Resolve_CaseInsensitiveRelativeUnits_Work()
    {
        var (startUs, _) = TimeRangeParser.Resolve(since: "2H AGO", until: null, Time(), defaultLookbackMinutes: 400);

        Assert.Equal(NowUs - 2L * 3600 * 1_000_000L, startUs);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
