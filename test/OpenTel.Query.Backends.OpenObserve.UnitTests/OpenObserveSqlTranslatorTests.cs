using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Backends.OpenObserve.UnitTests;

public class OpenObserveSqlTranslatorTests
{
    [Fact]
    public void ToTracePredicate_EmptySpec_ReturnsNull()
    {
        Assert.Null(OpenObserveSqlTranslator.ToTracePredicate(FilterSpec.Empty));
    }

    [Fact]
    public void ToTracePredicate_ServiceOnly_EqualsClause()
    {
        var spec = FilterSpec.Empty with { Service = "Api" };

        Assert.Equal("service_name = 'Api'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_OperationLike_UsesLike()
    {
        var spec = FilterSpec.Empty with
        {
            Operation = new OperationPattern("%Validate%", PatternMode.Like),
        };

        Assert.Equal("operation_name LIKE '%Validate%'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_OperationRegex_UsesReMatch()
    {
        var spec = FilterSpec.Empty with
        {
            Operation = new OperationPattern("^GET /statistik", PatternMode.Regex),
        };

        Assert.Equal("re_match(operation_name, '^GET /statistik')", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_OperationGlob_ConvertsGlobToLike()
    {
        var spec = FilterSpec.Empty with
        {
            Operation = new OperationPattern("GET */sta?", PatternMode.Glob),
        };

        Assert.Equal("operation_name LIKE 'GET %/sta_'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_Status_EmitsSpanStatusEquals()
    {
        var spec = FilterSpec.Empty with { Status = "ERROR" };

        Assert.Equal("span_status = 'ERROR'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_Attribute_EmitsColumnEquals()
    {
        var spec = FilterSpec.Empty with
        {
            Attributes = new[] { new AttributeFilter("http.request.method", "POST") },
        };

        Assert.Equal("http_request_method = 'POST'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_HttpStatusSingleCode_EmitsEqualsClause()
    {
        var spec = FilterSpec.Empty with { HttpStatus = HttpStatusParser.Parse("404") };

        Assert.Equal("http_response_status_code = 404", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_HttpStatusFiveXx_EmitsRangeClause()
    {
        var spec = FilterSpec.Empty with { HttpStatus = HttpStatusParser.Parse("5xx") };

        Assert.Equal("(http_response_status_code >= 500 AND http_response_status_code <= 599)", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_HttpStatusMixed_EmitsOrCombination()
    {
        var spec = FilterSpec.Empty with { HttpStatus = HttpStatusParser.Parse("404,5xx") };

        Assert.Equal(
            "(http_response_status_code = 404 OR (http_response_status_code >= 500 AND http_response_status_code <= 599))",
            OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_DurationGt_EmitsGreaterThanClause()
    {
        var spec = FilterSpec.Empty with { DurationGtUs = 500_000L };

        Assert.Equal("duration > 500000", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_MultipleFlags_JoinedWithAnd()
    {
        var spec = new FilterSpec(
            Service: "Api",
            Operation: new OperationPattern("GET%", PatternMode.Like),
            Status: "ERROR",
            Attributes: Array.Empty<AttributeFilter>(),
            HttpStatus: null,
            DurationGtUs: null);

        Assert.Equal("service_name = 'Api' AND operation_name LIKE 'GET%' AND span_status = 'ERROR'",
            OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_ValueWithApostrophe_IsEscaped()
    {
        var spec = FilterSpec.Empty with { Service = "It's Fine" };

        Assert.Equal("service_name = 'It''s Fine'", OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_AttributeKeyWithSemicolon_Throws()
    {
        var spec = FilterSpec.Empty with
        {
            Attributes = new[] { new AttributeFilter("foo; DROP TABLE", "x") },
        };

        Assert.Throws<InvalidOperationException>(() => OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_AttributeKeyEmpty_Throws()
    {
        var spec = FilterSpec.Empty with
        {
            Attributes = new[] { new AttributeFilter("", "x") },
        };

        Assert.Throws<InvalidOperationException>(() => OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void ToTracePredicate_OperationModeNone_Throws()
    {
        var spec = FilterSpec.Empty with
        {
            Operation = new OperationPattern("x", PatternMode.None),
        };

        Assert.Throws<InvalidOperationException>(() => OpenObserveSqlTranslator.ToTracePredicate(spec));
    }

    [Fact]
    public void GlobToLike_EscapesReservedCharacters()
    {
        Assert.Equal(@"100\% \_ \\ value", OpenObserveSqlTranslator.GlobToLike(@"100% _ \ value"));
    }

    [Fact]
    public void ToLogsPredicate_EmptySpec_ReturnsNullAndNull()
    {
        var (where, match) = OpenObserveSqlTranslator.ToLogsPredicate(LogsFilterSpec.Empty);

        Assert.Null(where);
        Assert.Null(match);
    }

    [Fact]
    public void ToLogsPredicate_AllEquals_AreAndedTogether()
    {
        var spec = LogsFilterSpec.Empty with { TraceId = "abc", Service = "Api", Level = "error" };

        var (where, _) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.NotNull(where);
        Assert.Contains("trace_id = 'abc'", where);
        Assert.Contains("service_name = 'Api'", where);
        Assert.Contains("severity = 'error'", where);
    }

    [Fact]
    public void ToLogsPredicate_MatchPassesThroughAndProducesNoWhereClause()
    {
        var spec = LogsFilterSpec.Empty with { Match = "exception" };

        var (where, match) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.Null(where);
        Assert.Equal("exception", match);
    }

    [Fact]
    public void ToLogsPredicate_FieldScopedLike_EmitsLikeClause()
    {
        var spec = LogsFilterSpec.Empty with
        {
            MatchField = "log",
            FieldMatch = new OperationPattern("%timeout%", PatternMode.Like),
        };

        var (where, _) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.Equal("log LIKE '%timeout%'", where);
    }

    [Fact]
    public void ToLogsPredicate_FieldScopedRegex_EmitsReMatch()
    {
        var spec = LogsFilterSpec.Empty with
        {
            MatchField = "message",
            FieldMatch = new OperationPattern("^E[0-9]+", PatternMode.Regex),
        };

        var (where, _) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.Equal("re_match(message, '^E[0-9]+')", where);
    }

    [Fact]
    public void ToLogsPredicate_FieldWithDotInName_NormalizedToUnderscore()
    {
        var spec = LogsFilterSpec.Empty with
        {
            MatchField = "http.route",
            FieldMatch = new OperationPattern("/statistik/*", PatternMode.Glob),
        };

        var (where, _) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.Equal("http_route LIKE '/statistik/%'", where);
    }

    [Fact]
    public void ToLogsPredicate_EscapesApostrophesInValues()
    {
        var spec = LogsFilterSpec.Empty with { TraceId = "O'Brien" };

        var (where, _) = OpenObserveSqlTranslator.ToLogsPredicate(spec);

        Assert.Equal("trace_id = 'O''Brien'", where);
    }
}
