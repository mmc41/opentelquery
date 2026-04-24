using System.Text.Json;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Core.UnitTests;

public class TraceAssemblerTests
{
    [Fact]
    public void SingleSpan_BuildsOneRootWithNoChildren()
    {
        var hit = Hit(
            traceId: "t1",
            spanId: "s1",
            parentSpanId: null,
            operation: "GET /ping",
            service: "Api",
            startNs: 1_000_000_000L,
            endNs:   1_001_000_000L,
            durationUs: 1_000,
            spanKind: "2",
            spanStatus: "UNSET");

        var traces = TraceAssembler.Assemble(new[] { hit });

        var trace = Assert.Single(traces);
        var root = Assert.Single(trace.RootSpans);
        Assert.Empty(root.Children);
        Assert.Equal("GET /ping", root.Operation);
        Assert.Equal("SERVER", root.Kind);
        Assert.Equal(2, root.KindCode);
        Assert.Equal(1.0, root.DurationMs);
        Assert.Equal(0, root.StartOffsetMs);
        Assert.Equal(1, trace.SpanCount);
        Assert.Equal(0, trace.ErrorCount);
    }

    [Fact]
    public void TwoLevelTree_NestsChildUnderParent()
    {
        var parent = Hit("t1", "root", null,    "PATCH /x",        "Api", 1_000_000_000, 1_050_000_000, 50_000);
        var child  = Hit("t1", "a",    "root",  "Validate",        "Api", 1_020_000_000, 1_022_000_000,  2_000);

        var traces = TraceAssembler.Assemble(new[] { parent, child });

        var trace = Assert.Single(traces);
        var root = Assert.Single(trace.RootSpans);
        Assert.Equal("root", root.SpanId);
        var kid = Assert.Single(root.Children);
        Assert.Equal("a", kid.SpanId);
        Assert.Equal("root", kid.ParentSpanId);
        Assert.Equal(20.0, kid.StartOffsetMs);
        Assert.Equal(2.0, kid.DurationMs);
    }

    [Fact]
    public void OrphanSpan_WhoseParentIsOutsideSet_BecomesARoot()
    {
        var known  = Hit("t1", "root", null,       "op", "Api", 1_000_000_000, 1_010_000_000, 10_000);
        var orphan = Hit("t1", "orph", "missing",  "op", "Api", 1_005_000_000, 1_006_000_000,  1_000);

        var traces = TraceAssembler.Assemble(new[] { known, orphan });

        var trace = Assert.Single(traces);
        Assert.Equal(2, trace.RootSpans.Count);
        Assert.Contains(trace.RootSpans, n => n.SpanId == "root");
        Assert.Contains(trace.RootSpans, n => n.SpanId == "orph");
    }

    [Fact]
    public void ChildrenAreSortedByStartOffsetAscending()
    {
        var parent = Hit("t1", "p", null,  "p", "Api", 0,          10_000_000, 10_000);
        var late   = Hit("t1", "late", "p", "late", "Api", 5_000_000, 6_000_000,  1_000);
        var early  = Hit("t1", "early", "p", "early", "Api", 1_000_000, 2_000_000, 1_000);

        var traces = TraceAssembler.Assemble(new[] { parent, late, early });

        var root = Assert.Single(traces).RootSpans.Single();
        Assert.Equal(new[] { "early", "late" }, root.Children.Select(c => c.SpanId).ToArray());
    }

    [Fact]
    public void ServicePrefixedKeys_LandInProcessNotAttributes()
    {
        var extras = new Dictionary<string, object?>
        {
            ["service_deployment_environment_name"] = "Development",
            ["service_telemetry_sdk_language"] = "dotnet",
            ["http_request_method"] = "GET",
            ["http_route"] = "/api/x",
        };
        var hit = Hit("t1", "s1", null, "GET /api/x", "Api", 1_000_000_000, 1_001_000_000, 1_000, extras: extras);

        var root = TraceAssembler.Assemble(new[] { hit }).Single().RootSpans.Single();

        Assert.Contains("service_deployment_environment_name", root.Process.Keys);
        Assert.Contains("service_telemetry_sdk_language", root.Process.Keys);
        Assert.Equal("Development", root.Process["service_deployment_environment_name"]);
        Assert.Contains("http_request_method", root.Attributes.Keys);
        Assert.Contains("http_route", root.Attributes.Keys);
        Assert.DoesNotContain("service_deployment_environment_name", root.Attributes.Keys);
    }

    [Fact]
    public void StructuralKeys_NeverAppearInAttributes()
    {
        var hit = Hit("t1", "s1", null, "op", "Api", 1_000_000_000, 1_001_000_000, 1_000);

        var root = TraceAssembler.Assemble(new[] { hit }).Single().RootSpans.Single();

        foreach (var forbidden in new[] { "trace_id", "span_id", "operation_name", "span_kind", "span_status", "start_time", "end_time", "duration", "events", "links", "flags", "_timestamp", "reference_parent_span_id" })
            Assert.DoesNotContain(forbidden, root.Attributes.Keys);
    }

    [Fact]
    public void SpanKind_IsDecodedToStringAndCode()
    {
        string[] expectedNames = ["UNSPECIFIED", "INTERNAL", "SERVER", "CLIENT", "PRODUCER", "CONSUMER"];
        for (var i = 0; i < expectedNames.Length; i++)
        {
            var hit = Hit("t1", $"s{i}", null, "op", "Api", 1_000_000_000, 1_001_000_000, 1_000, spanKind: i.ToString());
            var root = TraceAssembler.Assemble(new[] { hit }).Single().RootSpans.Single();
            Assert.Equal(expectedNames[i], root.Kind);
            Assert.Equal(i, root.KindCode);
        }
    }

    [Fact]
    public void ErrorSpan_IsCountedInErrorCount()
    {
        var ok  = Hit("t1", "a", null, "op", "Api", 1_000_000_000, 1_001_000_000, 1_000, spanStatus: "OK");
        var err = Hit("t1", "b", "a",  "op", "Api", 1_001_000_000, 1_002_000_000, 1_000, spanStatus: "ERROR");

        var trace = TraceAssembler.Assemble(new[] { ok, err }).Single();

        Assert.Equal(1, trace.ErrorCount);
        Assert.Equal(2, trace.SpanCount);
    }

    [Fact]
    public void ExceptionEvent_IsPromotedToExceptionsNotEvents()
    {
        var eventsJson = """
            [
              { "name": "pre-flight", "time_unix_nano": 1000000500, "attributes": { "foo": "bar" } },
              { "name": "exception",  "time_unix_nano": 1000000600,
                "attributes": {
                  "exception.type": "System.InvalidOperationException",
                  "exception.message": "bang",
                  "exception.stacktrace": "   at X.Y()"
                }
              }
            ]
            """;
        var hit = Hit("t1", "s1", null, "op", "Api", 1_000_000_000, 1_001_000_000, 1_000, eventsJson: eventsJson);

        var root = TraceAssembler.Assemble(new[] { hit }).Single().RootSpans.Single();

        var ev = Assert.Single(root.Events);
        Assert.Equal("pre-flight", ev.Name);
        var ex = Assert.Single(root.Exceptions);
        Assert.Equal("System.InvalidOperationException", ex.Type);
        Assert.Equal("bang", ex.Message);
        Assert.Contains("X.Y", ex.Stacktrace);
    }

    [Fact]
    public void EmptyEventsAndLinks_ParseAsEmptyArrays()
    {
        var hit = Hit("t1", "s1", null, "op", "Api", 1_000_000_000, 1_001_000_000, 1_000, eventsJson: "[]", linksJson: "[]");

        var root = TraceAssembler.Assemble(new[] { hit }).Single().RootSpans.Single();

        Assert.Empty(root.Events);
        Assert.Empty(root.Exceptions);
        Assert.Empty(root.Links);
    }

    [Fact]
    public void Aggregates_SupplyRootOperationAndServicesWhenPresent()
    {
        var hit = Hit("t1", "s1", null, "span-name", "Api", 1_000_000_000, 1_001_000_000, 1_000);
        var aggregates = new Dictionary<string, TraceAggregate>
        {
            ["t1"] = new(RootOperation: "override-op", RootService: "override-svc",
                         Services: new[] { new ServiceCount("Api", 1) })
        };

        var trace = TraceAssembler.Assemble(new[] { hit }, aggregates).Single();

        Assert.Equal("override-op", trace.RootOperation);
        Assert.Equal("override-svc", trace.RootService);
        Assert.Equal("Api", trace.Services[0].Name);
    }

    [Fact]
    public void NoAggregates_DerivesServicesFromSpans()
    {
        var a = Hit("t1", "a", null, "op", "Api",     1_000_000_000, 1_001_000_000, 1_000);
        var b = Hit("t1", "b", "a",  "op", "Storage", 1_001_000_000, 1_002_000_000, 1_000);
        var c = Hit("t1", "c", "a",  "op", "Storage", 1_002_000_000, 1_003_000_000, 1_000);

        var trace = TraceAssembler.Assemble(new[] { a, b, c }).Single();

        var services = trace.Services.ToDictionary(s => s.Name, s => s.SpanCount);
        Assert.Equal(1, services["Api"]);
        Assert.Equal(2, services["Storage"]);
    }

    private static JsonElement Hit(
        string traceId, string spanId, string? parentSpanId,
        string operation, string service,
        long startNs, long endNs, long durationUs,
        string spanKind = "1",
        string spanStatus = "UNSET",
        string eventsJson = "[]",
        string linksJson = "[]",
        Dictionary<string, object?>? extras = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["_timestamp"] = startNs / 1000,
            ["trace_id"] = traceId,
            ["span_id"] = spanId,
            ["reference_parent_span_id"] = parentSpanId ?? "",
            ["operation_name"] = operation,
            ["service_name"] = service,
            ["start_time"] = startNs,
            ["end_time"] = endNs,
            ["duration"] = durationUs,
            ["span_kind"] = spanKind,
            ["span_status"] = spanStatus,
            ["flags"] = 1,
            ["events"] = eventsJson,
            ["links"] = linksJson,
        };
        if (extras is not null)
            foreach (var kv in extras)
                payload[kv.Key] = kv.Value;

        var json = JsonSerializer.Serialize(payload);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
