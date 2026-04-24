using System.Net;
using System.Text.Json;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Backends.OpenObserve.UnitTests;

public class OpenObserveBackendTests
{
    private static OpenObserveSettings DefaultSettings() => new(
        Host: new Uri("https://openobserve.test"),
        Authorization: "Basic dGVzdA==",
        Organization: "acme",
        StreamName: "default");

    [Fact]
    public async Task GetLatestTracesAsync_BuildsExpectedUrlAndHeaders()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var body = await backend.GetLatestTracesAsync(startTimeUs: 1000, endTimeUs: 2000, from: 5, size: 25, TestContext.Current.CancellationToken);

        Assert.Equal("{\"hits\":[]}", body);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "https://openobserve.test/api/acme/default/traces/latest?start_time=1000&end_time=2000&from=5&size=25",
            handler.LastRequest.RequestUri!.ToString());

        var auth = handler.LastRequest.Headers.GetValues("Authorization").Single();
        Assert.Equal("Basic dGVzdA==", auth);
    }

    [Fact]
    public async Task SearchTraceSpansAsync_BuildsInClauseWithEscapingAndTypeTraces()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.SearchTraceSpansAsync(new[] { "id1", "id'2" }, startTimeUs: 100, endTimeUs: 200, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://openobserve.test/api/acme/_search?type=traces", handler.LastRequest.RequestUri!.ToString());

        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(requestBody);
        var sql = doc.RootElement.GetProperty("query").GetProperty("sql").GetString()!;
        Assert.Contains("trace_id IN ('id1','id''2')", sql);
        Assert.Contains("FROM \"default\"", sql);
        Assert.Contains("ORDER BY _timestamp ASC", sql);
        Assert.Equal(100, doc.RootElement.GetProperty("query").GetProperty("start_time").GetInt64());
        Assert.Equal(200, doc.RootElement.GetProperty("query").GetProperty("end_time").GetInt64());
    }

    [Fact]
    public async Task SearchTraceSpansAsync_EmptyIdSet_ReturnsEmptyEnvelopeWithoutHttp()
    {
        var handler = new RecordingHandler("{\"should\":\"not-be-called\"}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var body = await backend.SearchTraceSpansAsync(Array.Empty<string>(), 0, 1, TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest);
        Assert.Contains("\"hits\":[]", body);
    }

    [Fact]
    public async Task SearchFilteredTraceIdsAsync_EmitsGroupedTraceIdQueryWithinTimeRange()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var filter = FilterSpec.Empty with { Service = "Api" };
        await backend.SearchFilteredTraceIdsAsync(
            filter,
            startTimeUs: 100, endTimeUs: 200,
            from: 0, size: 50,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://openobserve.test/api/acme/_search?type=traces", handler.LastRequest.RequestUri!.ToString());

        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(requestBody);
        var query = doc.RootElement.GetProperty("query");
        var sql = query.GetProperty("sql").GetString()!;
        Assert.Contains("SELECT trace_id", sql);
        Assert.Contains("WHERE service_name = 'Api'", sql);
        Assert.Contains("GROUP BY trace_id", sql);
        Assert.Equal(100, query.GetProperty("start_time").GetInt64());
        Assert.Equal(200, query.GetProperty("end_time").GetInt64());
        Assert.Equal(0, query.GetProperty("from").GetInt32());
        Assert.Equal(50, query.GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task SearchFilteredTraceIdsAsync_EmptyFilter_FallsBackToTautology()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.SearchFilteredTraceIdsAsync(
            FilterSpec.Empty,
            startTimeUs: 1, endTimeUs: 2,
            from: 0, size: 10,
            TestContext.Current.CancellationToken);

        var requestBody = await handler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(requestBody);
        Assert.Contains("WHERE 1 = 1", doc.RootElement.GetProperty("query").GetProperty("sql").GetString());
    }

    [Fact]
    public async Task SearchLogsAsync_CombinesWhereAndMatchAll()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var filter = LogsFilterSpec.Empty with { Service = "Api", Match = "exception" };
        await backend.SearchLogsAsync(
            filter,
            startTimeUs: 10, endTimeUs: 20,
            from: 0, size: 25,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://openobserve.test/api/acme/_search?type=logs", handler.LastRequest.RequestUri!.ToString());

        var requestBody = await handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(requestBody);
        var sql = doc.RootElement.GetProperty("query").GetProperty("sql").GetString()!;
        Assert.Contains("service_name = 'Api'", sql);
        Assert.Contains("match_all('exception')", sql);
        Assert.Contains("ORDER BY _timestamp DESC", sql);
    }

    [Fact]
    public async Task SearchLogsAsync_EscapesApostropheInMatch()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var filter = LogsFilterSpec.Empty with { Match = "can't" };
        await backend.SearchLogsAsync(
            filter,
            startTimeUs: 0, endTimeUs: 1, from: 0, size: 10,
            TestContext.Current.CancellationToken);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        Assert.Contains("match_all('can''t')", doc.RootElement.GetProperty("query").GetProperty("sql").GetString());
    }

    [Fact]
    public async Task GetAroundAsync_ConstructsExpectedUrl()
    {
        var handler = new RecordingHandler("{\"hits\":[]}");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.GetAroundAsync(streamName: "default", streamType: "logs", keyUs: 1_700_000_000_000_000L, size: 20, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "https://openobserve.test/api/acme/default/_around?key=1700000000000000&size=20&type=logs",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListStreamsAsync_ConstructsExpectedUrl()
    {
        var handler = new RecordingHandler("""{"list":[]}""");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.ListStreamsAsync(streamType: "logs", fetchSchema: true, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(
            "https://openobserve.test/api/acme/streams?fetchSchema=true&type=logs",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListStreamsAsync_NoTypeFilter_OmitsTypeParam()
    {
        var handler = new RecordingHandler("""{"list":[]}""");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.ListStreamsAsync(streamType: null, fetchSchema: false, TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://openobserve.test/api/acme/streams?fetchSchema=false",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetStreamSchemaAsync_ConstructsExpectedUrl()
    {
        var handler = new RecordingHandler("""{"schema":[]}""");
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        await backend.GetStreamSchemaAsync(streamName: "default", streamType: "traces", TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://openobserve.test/api/acme/streams/default/schema?type=traces",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task NonSuccessResponse_ThrowsTelemetryBackendException()
    {
        var handler = new RecordingHandler("{\"error\":\"nope\"}", HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        using var backend = new OpenObserveBackend(DefaultSettings(), http);

        var ex = await Assert.ThrowsAsync<TelemetryBackendException>(
            () => backend.GetLatestTracesAsync(0, 1, 0, 10, TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("openobserve", ex.BackendName);
        Assert.Equal("{\"error\":\"nope\"}", ex.ResponseBody);
        Assert.Contains("Authorization", ex.Hint);
    }

    [Fact]
    public void Properties_ExposeOrganizationAndStream()
    {
        using var backend = new OpenObserveBackend(DefaultSettings(), new HttpClient(new RecordingHandler("{}")));

        Assert.Equal("acme", backend.Properties["organization"]);
        Assert.Equal("default", backend.Properties["stream"]);
        Assert.Equal("openobserve", backend.BackendName);
        Assert.Equal("default", backend.DefaultStreamName);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _status;

        public HttpRequestMessage? LastRequest { get; private set; }

        public RecordingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var buffered = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                request.Content = new ByteArrayContent(buffered) { Headers = { ContentType = request.Content.Headers.ContentType } };
            }
            LastRequest = request;
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
