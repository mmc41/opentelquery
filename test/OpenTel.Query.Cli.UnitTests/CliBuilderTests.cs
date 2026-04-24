using System.Text.Json;
using OpenTel.Query.Cli;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Cli.UnitTests;

public class CliBuilderTests
{
    private const string SampleLatestJson = """
        {
          "hits": [
            {
              "trace_id": "t1",
              "first_event": { "operation_name": "GET /x", "service_name": "Api" },
              "service_name": [ { "service_name": "Api", "count": 1 } ]
            }
          ]
        }
        """;

    private const string SampleSpansJson = """
        {
          "hits": [
            {
              "_timestamp": 1000000,
              "trace_id": "t1",
              "span_id": "s1",
              "reference_parent_span_id": "",
              "operation_name": "GET /x",
              "service_name": "Api",
              "start_time": 1000000000,
              "end_time":   1001000000,
              "duration":   1000,
              "span_kind":  "2",
              "span_status":"UNSET",
              "flags": 1,
              "events": "[]",
              "links":  "[]"
            }
          ]
        }
        """;

    private static QuerySettings Settings() => new(LookbackMinutes: 400);

    [Fact]
    public async Task Query_NoOptions_DefaultSizeIs50AndBundleShapeIsEmitted()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.GetLatestTracesCalled);
        Assert.True(fake.SearchTraceSpansCalled);
        Assert.Equal(50, fake.LastSize);
        Assert.Equal(0, fake.LastFrom);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var r = doc.RootElement;
        Assert.Equal("opentel-query-trace/v1", r.GetProperty("$schema").GetString());
        Assert.Equal("query", r.GetProperty("command").GetString());
        Assert.Equal("fake", r.GetProperty("source").GetProperty("backend").GetString());
        Assert.Equal("acme", r.GetProperty("source").GetProperty("properties").GetProperty("organization").GetString());
        Assert.Null(r.GetProperty("query_info").GetProperty("trace_id").GetString());
        Assert.Equal(50, r.GetProperty("query_info").GetProperty("requested_size").GetInt32());
        var traces = r.GetProperty("traces");
        Assert.Equal(1, traces.GetArrayLength());
        Assert.Equal("t1", traces[0].GetProperty("trace_id").GetString());
        var rootSpans = traces[0].GetProperty("root_spans");
        Assert.Equal(1, rootSpans.GetArrayLength());
        Assert.Equal("SERVER", rootSpans[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Query_SinceAndSizeOverrides_Apply()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(2_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query", "--since", "5m ago", "--size", "7", "--from", "10" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(7, fake.LastSize);
        Assert.Equal(10, fake.LastFrom);
        Assert.Equal(2_000_000_000L - 5L * 60 * 1_000_000, fake.LastStartUs);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var window = doc.RootElement.GetProperty("window");
        Assert.Equal(5, window.GetProperty("lookback_minutes").GetInt32());
    }

    [Fact]
    public async Task Query_EmptyLatestResult_EmitsBundleWithNoTraces()
    {
        var fake = new FakeBackend("{\"hits\":[]}", SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.False(fake.SearchTraceSpansCalled);
        using var doc = JsonDocument.Parse(stdout.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("traces").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("query_info").GetProperty("returned").GetInt32());
    }

    [Fact]
    public async Task Lookup_PassesTraceIdAndEmitsSameBundleShape()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(3_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "lookup", "abc123" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.SearchTraceSpansCalled);
        Assert.Contains("abc123", fake.LastTraceIds!);
        Assert.Equal(3_000_000_000L, fake.LastEndUs);
        Assert.Equal(3_000_000_000L - 400L * 60 * 1_000_000, fake.LastStartUs);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var r = doc.RootElement;
        Assert.Equal("lookup", r.GetProperty("command").GetString());
        Assert.Equal("abc123", r.GetProperty("query_info").GetProperty("trace_id").GetString());
        Assert.Equal(1, r.GetProperty("query_info").GetProperty("requested_size").GetInt32());
        Assert.Equal(1, r.GetProperty("traces").GetArrayLength());
    }

    [Fact]
    public async Task Query_WithServiceFilter_UsesFilteredPathAndEchoesFilters()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            FilteredBody = """{ "hits": [ { "trace_id": "t1" } ] }""",
        };
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query", "--service", "Api" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.SearchFilteredTraceIdsCalled);
        Assert.False(fake.GetLatestTracesCalled);
        Assert.NotNull(fake.LastFilter);
        Assert.Equal("Api", fake.LastFilter!.Service);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var filters = doc.RootElement.GetProperty("query_info").GetProperty("filters");
        Assert.Equal("Api", filters.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Query_WithOpLikeAndOpRegex_IsMutuallyExclusive()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        Console.SetError(new StringWriter());

        var root = CliBuilder.Build(() => fake, Settings(), time, TextWriter.Null);
        var exit = await root.Parse(new[] { "query", "--op-like", "%x%", "--op-regex", ".*x.*" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(fake.SearchFilteredTraceIdsCalled);
    }

    [Fact]
    public async Task Query_WithRepeatedAttrOptions_BothAppearInFilter()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            FilteredBody = """{"hits": []}""",
        };
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query", "--attr", "http.route=/api/foo", "--attr", "user.id=42" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.NotNull(fake.LastFilter);
        Assert.Equal(2, fake.LastFilter!.Attributes.Count);
        Assert.Contains(fake.LastFilter.Attributes, a => a.Key == "http.route" && a.Value == "/api/foo");
        Assert.Contains(fake.LastFilter.Attributes, a => a.Key == "user.id" && a.Value == "42");
    }

    [Fact]
    public async Task Query_WithDurationGtAndHttpStatus_PopulatesFilter()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            FilteredBody = """{"hits": []}""",
        };
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query", "--duration-gt", "500ms", "--http-status", "5xx" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.NotNull(fake.LastFilter);
        Assert.Equal(500_000L, fake.LastFilter!.DurationGtUs);
        Assert.NotNull(fake.LastFilter.HttpStatus);
        Assert.Equal((500, 599), fake.LastFilter.HttpStatus!.Ranges[0]);
    }

    [Fact]
    public async Task Query_InvalidAttrFormat_FailsWithNonZeroExit()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        Console.SetError(new StringWriter());

        var root = CliBuilder.Build(() => fake, Settings(), time, TextWriter.Null);
        var exit = await root.Parse(new[] { "query", "--attr", "no-equals-sign" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(fake.SearchFilteredTraceIdsCalled);
    }

    [Fact]
    public async Task Query_NoFilters_DoesNotEmitFiltersNodeContent()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "query" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout.ToString());
        var filters = doc.RootElement.GetProperty("query_info").GetProperty("filters");
        Assert.Equal(JsonValueKind.Null, filters.ValueKind);
    }

    [Fact]
    public async Task Lookup_WithSinceAndUntil_UsesExplicitWindow()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 04, 24, 12, 0, 0, TimeSpan.Zero));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "lookup", "abc123", "--since", "2026-04-20T10:00:00Z", "--until", "2026-04-20T12:00:00Z" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        var expectedStart = new DateTimeOffset(2026, 04, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;
        var expectedEnd = new DateTimeOffset(2026, 04, 20, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;
        Assert.Equal(expectedStart, fake.LastStartUs);
        Assert.Equal(expectedEnd, fake.LastEndUs);
    }

    [Fact]
    public async Task Logs_WithTraceIdAndMatch_EmitsLogBundle()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            LogsBody = """
                {
                  "hits": [
                    { "_timestamp": 1700000000000000, "trace_id": "abc", "message": "boom", "level": "error", "service_name": "Api" }
                  ]
                }
                """,
        };
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 04, 24, 12, 0, 0, TimeSpan.Zero));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "logs", "--trace-id", "abc", "--match", "boom" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.SearchLogsCalled);
        Assert.Equal("abc", fake.LastLogsFilter!.TraceId);
        Assert.Equal("boom", fake.LastLogsFilter.Match);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var r = doc.RootElement;
        Assert.Equal("opentel-query-log/v1", r.GetProperty("$schema").GetString());
        Assert.Equal("logs", r.GetProperty("command").GetString());
        var logs = r.GetProperty("logs");
        Assert.Equal(1, logs.GetArrayLength());
        Assert.Equal("abc", logs[0].GetProperty("trace_id").GetString());
        Assert.Equal("boom", logs[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Around_WithIsoTimestamp_CallsBackendWithMicros()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            AroundBody = """{"hits":[]}""",
        };
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 04, 24, 12, 0, 0, TimeSpan.Zero));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "around", "--at", "2026-04-23T13:38:00Z", "--size", "20" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.GetAroundCalled);
        Assert.Equal("default", fake.LastAroundStream);
        Assert.Equal("logs", fake.LastAroundStreamType);
        Assert.Equal(new DateTimeOffset(2026, 04, 23, 13, 38, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L, fake.LastAroundKey);
        Assert.Equal(20, fake.LastAroundSize);
    }

    [Fact]
    public async Task Around_InvalidStreamType_FailsWithNonZeroExit()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 04, 24, 12, 0, 0, TimeSpan.Zero));
        Console.SetError(new StringWriter());

        var root = CliBuilder.Build(() => fake, Settings(), time, TextWriter.Null);
        var exit = await root.Parse(new[] { "around", "--at", "2026-04-23T13:38:00Z", "--stream-type", "metrics" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(fake.GetAroundCalled);
    }

    [Fact]
    public async Task Streams_WithType_AndFetchSchema_EmitsStreamsBundle()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            StreamsBody = """
                {
                  "list": [
                    {
                      "name": "default",
                      "stream_type": "traces",
                      "storage_type": "s3",
                      "stats": { "doc_num": 100 },
                      "settings": { "partition_keys": {}, "full_text_search_keys": ["body"] },
                      "schema": [ { "name": "_timestamp", "type": "Int64" } ]
                    }
                  ]
                }
                """,
        };
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "streams", "--type", "traces", "--fetch-schema" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.ListStreamsCalled);
        Assert.Equal("traces", fake.LastStreamsType);
        Assert.True(fake.LastStreamsFetchSchema);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var r = doc.RootElement;
        Assert.Equal("opentel-query-streams/v1", r.GetProperty("$schema").GetString());
        Assert.Equal("streams", r.GetProperty("command").GetString());
        var streams = r.GetProperty("streams");
        Assert.Equal(1, streams.GetArrayLength());
        Assert.Equal("default", streams[0].GetProperty("name").GetString());
        Assert.Equal("Int64", streams[0].GetProperty("schema")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Schema_WithValidType_EmitsSchemaBundle()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson)
        {
            SchemaBody = """
                {
                  "name": "default",
                  "stream_type": "traces",
                  "schema": [
                    { "name": "trace_id", "type": "Utf8" },
                    { "name": "duration", "type": "Int64" }
                  ],
                  "settings": { "partition_keys": {}, "full_text_search_keys": [] }
                }
                """,
        };
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        var stdout = new StringWriter();

        var root = CliBuilder.Build(() => fake, Settings(), time, stdout);
        var exit = await root.Parse(new[] { "schema", "default", "--type", "traces" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(fake.GetStreamSchemaCalled);
        Assert.Equal("default", fake.LastSchemaStream);
        Assert.Equal("traces", fake.LastSchemaType);

        using var doc = JsonDocument.Parse(stdout.ToString());
        var r = doc.RootElement;
        Assert.Equal("opentel-query-schema/v1", r.GetProperty("$schema").GetString());
        Assert.Equal("default", r.GetProperty("stream").GetString());
        Assert.Equal("traces", r.GetProperty("stream_type").GetString());
        var fields = r.GetProperty("fields");
        Assert.Equal(2, fields.GetArrayLength());
    }

    [Fact]
    public async Task Schema_InvalidType_FailsWithNonZeroExit()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_000_000));
        Console.SetError(new StringWriter());

        var root = CliBuilder.Build(() => fake, Settings(), time, TextWriter.Null);
        var exit = await root.Parse(new[] { "schema", "default", "--type", "flibble" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(fake.GetStreamSchemaCalled);
    }

    [Fact]
    public async Task Lookup_MissingTraceId_FailsWithNonZeroExit()
    {
        var fake = new FakeBackend(SampleLatestJson, SampleSpansJson);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Console.SetError(new StringWriter());

        var root = CliBuilder.Build(() => fake, Settings(), time, TextWriter.Null);
        var exit = await root.Parse(new[] { "lookup" }).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(fake.SearchTraceSpansCalled);
    }

    private sealed class FakeBackend : ITelemetryBackend
    {
        private readonly string _latestBody;
        private readonly string _spansBody;

        public bool GetLatestTracesCalled { get; private set; }
        public bool SearchTraceSpansCalled { get; private set; }
        public long LastStartUs { get; private set; }
        public long LastEndUs { get; private set; }
        public int LastFrom { get; private set; }
        public int LastSize { get; private set; }
        public IReadOnlyCollection<string>? LastTraceIds { get; private set; }

        public FakeBackend(string latestBody, string spansBody)
        {
            _latestBody = latestBody;
            _spansBody = spansBody;
        }

        public string BackendName => "fake";

        public string Host => "https://fake.test";

        public IReadOnlyDictionary<string, string?> Properties => new Dictionary<string, string?>
        {
            ["organization"] = "acme",
            ["stream"] = "default",
        };

        public string DefaultStreamName => "default";

        public Task<string> GetLatestTracesAsync(long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
        {
            GetLatestTracesCalled = true;
            LastStartUs = startTimeUs;
            LastEndUs = endTimeUs;
            LastFrom = from;
            LastSize = size;
            return Task.FromResult(_latestBody);
        }

        public Task<string> SearchTraceSpansAsync(IReadOnlyCollection<string> traceIds, long startTimeUs, long endTimeUs, CancellationToken ct)
        {
            SearchTraceSpansCalled = true;
            LastTraceIds = traceIds;
            LastStartUs = startTimeUs;
            LastEndUs = endTimeUs;
            return Task.FromResult(_spansBody);
        }

        public string? FilteredBody { get; set; }
        public FilterSpec? LastFilter { get; private set; }
        public bool SearchFilteredTraceIdsCalled { get; private set; }

        public Task<string> SearchFilteredTraceIdsAsync(FilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
        {
            SearchFilteredTraceIdsCalled = true;
            LastFilter = filter;
            LastStartUs = startTimeUs;
            LastEndUs = endTimeUs;
            LastFrom = from;
            LastSize = size;
            return Task.FromResult(FilteredBody ?? _latestBody);
        }

        public string? LogsBody { get; set; }
        public LogsFilterSpec? LastLogsFilter { get; private set; }
        public bool SearchLogsCalled { get; private set; }

        public Task<string> SearchLogsAsync(LogsFilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
        {
            SearchLogsCalled = true;
            LastLogsFilter = filter;
            LastStartUs = startTimeUs;
            LastEndUs = endTimeUs;
            LastFrom = from;
            LastSize = size;
            return Task.FromResult(LogsBody ?? """{"hits":[]}""");
        }

        public string? AroundBody { get; set; }
        public string? LastAroundStream { get; private set; }
        public string? LastAroundStreamType { get; private set; }
        public long LastAroundKey { get; private set; }
        public int LastAroundSize { get; private set; }
        public bool GetAroundCalled { get; private set; }

        public Task<string> GetAroundAsync(string streamName, string streamType, long keyUs, int size, CancellationToken ct)
        {
            GetAroundCalled = true;
            LastAroundStream = streamName;
            LastAroundStreamType = streamType;
            LastAroundKey = keyUs;
            LastAroundSize = size;
            return Task.FromResult(AroundBody ?? """{"hits":[]}""");
        }

        public string? StreamsBody { get; set; }
        public string? LastStreamsType { get; private set; }
        public bool LastStreamsFetchSchema { get; private set; }
        public bool ListStreamsCalled { get; private set; }

        public Task<string> ListStreamsAsync(string? streamType, bool fetchSchema, CancellationToken ct)
        {
            ListStreamsCalled = true;
            LastStreamsType = streamType;
            LastStreamsFetchSchema = fetchSchema;
            return Task.FromResult(StreamsBody ?? """{"list":[]}""");
        }

        public string? SchemaBody { get; set; }
        public string? LastSchemaStream { get; private set; }
        public string? LastSchemaType { get; private set; }
        public bool GetStreamSchemaCalled { get; private set; }

        public Task<string> GetStreamSchemaAsync(string streamName, string streamType, CancellationToken ct)
        {
            GetStreamSchemaCalled = true;
            LastSchemaStream = streamName;
            LastSchemaType = streamType;
            return Task.FromResult(SchemaBody ?? """{"name":"x","stream_type":"logs","schema":[]}""");
        }

        public void Dispose() { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
