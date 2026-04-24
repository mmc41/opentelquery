using OpenTel.Query.Cli.Commands;

namespace OpenTel.Query.Cli.UnitTests;

public class LogsCommandTests
{
    [Fact]
    public void BuildLogsFilterSpec_NoFilters_ReturnsEmpty()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: null, service: null, level: null,
            match: null, matchField: null,
            matchLike: null, matchRegex: null, matchGlob: null);

        Assert.True(spec.IsEmpty);
    }

    [Fact]
    public void BuildLogsFilterSpec_AllEqualsFieldsPreserved()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: "abc", service: "Api", level: "error",
            match: null, matchField: null,
            matchLike: null, matchRegex: null, matchGlob: null);

        Assert.Equal("abc", spec.TraceId);
        Assert.Equal("Api", spec.Service);
        Assert.Equal("error", spec.Level);
        Assert.Null(spec.FieldMatch);
        Assert.Null(spec.Match);
    }

    [Fact]
    public void BuildLogsFilterSpec_MatchPassesThrough()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: null, service: null, level: null,
            match: "exception", matchField: null,
            matchLike: null, matchRegex: null, matchGlob: null);

        Assert.Equal("exception", spec.Match);
        Assert.Null(spec.FieldMatch);
    }

    [Fact]
    public void BuildLogsFilterSpec_FieldScopedLike_CapturesPattern()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: null, service: null, level: null,
            match: null, matchField: "log",
            matchLike: "%timeout%", matchRegex: null, matchGlob: null);

        Assert.Equal("log", spec.MatchField);
        Assert.NotNull(spec.FieldMatch);
        Assert.Equal("%timeout%", spec.FieldMatch!.Pattern);
        Assert.Equal(OpenTel.Query.Core.Filtering.PatternMode.Like, spec.FieldMatch.Mode);
    }

    [Fact]
    public void BuildLogsFilterSpec_FieldScopedRegex_CapturesPattern()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: null, service: null, level: null,
            match: null, matchField: "message",
            matchLike: null, matchRegex: "^E[0-9]+", matchGlob: null);

        Assert.Equal(OpenTel.Query.Core.Filtering.PatternMode.Regex, spec.FieldMatch!.Mode);
        Assert.Equal("^E[0-9]+", spec.FieldMatch.Pattern);
    }

    [Fact]
    public void BuildLogsFilterSpec_FieldScopedGlob_CapturesPattern()
    {
        var spec = LogsCommand.BuildLogsFilterSpec(
            traceId: null, service: null, level: null,
            match: null, matchField: "http.route",
            matchLike: null, matchRegex: null, matchGlob: "/statistik/*");

        Assert.Equal("http.route", spec.MatchField);
        Assert.Equal(OpenTel.Query.Core.Filtering.PatternMode.Glob, spec.FieldMatch!.Mode);
        Assert.Equal("/statistik/*", spec.FieldMatch.Pattern);
    }

    [Fact]
    public void BuildLogsFilterSpec_MultipleMatchModes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LogsCommand.BuildLogsFilterSpec(
                traceId: null, service: null, level: null,
                match: null, matchField: "log",
                matchLike: "%a%", matchRegex: "a.*", matchGlob: null));
    }

    [Fact]
    public void BuildLogsFilterSpec_MatchFieldWithoutMode_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LogsCommand.BuildLogsFilterSpec(
                traceId: null, service: null, level: null,
                match: null, matchField: "log",
                matchLike: null, matchRegex: null, matchGlob: null));
    }

    [Fact]
    public void BuildLogsFilterSpec_ModeWithoutField_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LogsCommand.BuildLogsFilterSpec(
                traceId: null, service: null, level: null,
                match: null, matchField: null,
                matchLike: "%a%", matchRegex: null, matchGlob: null));
    }

    [Fact]
    public void BuildLogsFilterSpec_MatchAndFieldTogether_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LogsCommand.BuildLogsFilterSpec(
                traceId: null, service: null, level: null,
                match: "foo", matchField: "log",
                matchLike: "%bar%", matchRegex: null, matchGlob: null));
    }
}
