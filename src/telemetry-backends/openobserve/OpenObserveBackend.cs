using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Backends.OpenObserve;

public sealed class OpenObserveBackend : ITelemetryBackend
{
    public const string Name = "openobserve";

    private readonly HttpClient _http;
    private readonly OpenObserveSettings _settings;
    private readonly bool _ownsClient;

    public OpenObserveBackend(OpenObserveSettings settings)
        : this(settings, new HttpClient { BaseAddress = settings.Host }, ownsClient: true)
    {
    }

    public OpenObserveBackend(OpenObserveSettings settings, HttpClient httpClient, bool ownsClient = false)
    {
        _settings = settings;
        _http = httpClient;
        _ownsClient = ownsClient;

        if (_http.BaseAddress is null)
            _http.BaseAddress = settings.Host;

        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", settings.Authorization);
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    public static OpenObserveBackend Create(IConfiguration cfg) =>
        new(OpenObserveSettings.Load(cfg));

    public string BackendName => Name;

    public string Host => _settings.Host.ToString();

    public IReadOnlyDictionary<string, string?> Properties => new Dictionary<string, string?>
    {
        ["organization"] = _settings.Organization,
        ["stream"] = _settings.StreamName,
    };

    public string DefaultStreamName => _settings.StreamName;

    public async Task<string> GetLatestTracesAsync(long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
    {
        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/{Uri.EscapeDataString(_settings.StreamName)}/traces/latest"
            + $"?start_time={startTimeUs}&end_time={endTimeUs}&from={from}&size={size}";

        using var response = await _http.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, body, $"organization='{_settings.Organization}' stream-name='{_settings.StreamName}'");
        return body;
    }

    public async Task<string> SearchTraceSpansAsync(IReadOnlyCollection<string> traceIds, long startTimeUs, long endTimeUs, CancellationToken ct)
    {
        if (traceIds.Count == 0)
            return "{\"hits\":[],\"total\":0}";

        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/_search?type=traces";
        var idList = string.Join(",", traceIds.Select(id => $"'{OpenObserveSqlTranslator.EscapeLiteral(id)}'"));
        var sql = $"SELECT * FROM \"{_settings.StreamName}\" WHERE trace_id IN ({idList}) ORDER BY _timestamp ASC";

        var body = new
        {
            query = new
            {
                sql,
                start_time = startTimeUs,
                end_time = endTimeUs,
                from = 0,
                size = 10000,
            },
            search_type = "ui",
        };

        using var response = await _http.PostAsJsonAsync(path, body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"trace_ids=[{idList}]");
        return responseBody;
    }

    public async Task<string> SearchFilteredTraceIdsAsync(FilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
    {
        var predicate = OpenObserveSqlTranslator.ToTracePredicate(filter);
        var where = string.IsNullOrWhiteSpace(predicate) ? "1 = 1" : predicate!;

        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/_search?type=traces";
        var sql = $"SELECT trace_id, MAX(_timestamp) AS last_seen FROM \"{_settings.StreamName}\" WHERE {where} "
            + "GROUP BY trace_id ORDER BY last_seen DESC";

        var body = new
        {
            query = new
            {
                sql,
                start_time = startTimeUs,
                end_time = endTimeUs,
                from,
                size,
            },
            search_type = "ui",
        };

        using var response = await _http.PostAsJsonAsync(path, body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"filter='{where}'");
        return responseBody;
    }

    public async Task<string> SearchLogsAsync(LogsFilterSpec filter, long startTimeUs, long endTimeUs, int from, int size, CancellationToken ct)
    {
        var (wherePredicate, matchAll) = OpenObserveSqlTranslator.ToLogsPredicate(filter);

        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/_search?type=logs";

        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(wherePredicate))
            clauses.Add(wherePredicate!);
        if (!string.IsNullOrWhiteSpace(matchAll))
            clauses.Add($"match_all('{OpenObserveSqlTranslator.EscapeLiteral(matchAll!)}')");

        var where = clauses.Count == 0 ? "1 = 1" : string.Join(" AND ", clauses);
        var sql = $"SELECT * FROM \"{_settings.StreamName}\" WHERE {where} ORDER BY _timestamp DESC";

        var body = new
        {
            query = new
            {
                sql,
                start_time = startTimeUs,
                end_time = endTimeUs,
                from,
                size,
            },
            search_type = "ui",
        };

        using var response = await _http.PostAsJsonAsync(path, body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"logs filter='{where}'");
        return responseBody;
    }

    public async Task<string> GetAroundAsync(string streamName, string streamType, long keyUs, int size, CancellationToken ct)
    {
        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/{Uri.EscapeDataString(streamName)}/_around"
            + $"?key={keyUs}&size={size}&type={Uri.EscapeDataString(streamType)}";

        using var response = await _http.GetAsync(path, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"stream='{streamName}' type='{streamType}'");
        return responseBody;
    }

    public async Task<string> ListStreamsAsync(string? streamType, bool fetchSchema, CancellationToken ct)
    {
        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/streams?fetchSchema={(fetchSchema ? "true" : "false")}";
        if (!string.IsNullOrWhiteSpace(streamType))
            path += $"&type={Uri.EscapeDataString(streamType)}";

        using var response = await _http.GetAsync(path, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"streams type='{streamType ?? "(any)"}'");
        return responseBody;
    }

    public async Task<string> GetStreamSchemaAsync(string streamName, string streamType, CancellationToken ct)
    {
        var path = $"/api/{Uri.EscapeDataString(_settings.Organization)}/streams/{Uri.EscapeDataString(streamName)}/schema"
            + $"?type={Uri.EscapeDataString(streamType)}";

        using var response = await _http.GetAsync(path, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody, $"stream='{streamName}' type='{streamType}'");
        return responseBody;
    }

    private void EnsureSuccess(HttpResponseMessage response, string body, string context)
    {
        if (response.IsSuccessStatusCode) return;

        var hint = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Check Telemetry:Headers Authorization value in user secrets.",
            System.Net.HttpStatusCode.NotFound => $"Check {context}.",
            _ => null,
        };
        throw new TelemetryBackendException(Name, (int)response.StatusCode, response.ReasonPhrase ?? "", body, hint);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
