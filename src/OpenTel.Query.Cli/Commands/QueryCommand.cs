using System.CommandLine;
using System.Text.Json;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Filtering;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class QueryCommand
{
    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var (sinceOpt, untilOpt) = TimeRangeOptions.Create();
        var sizeOpt = new Option<int>("--size")
        {
            Description = "Maximum number of traces to return.",
            DefaultValueFactory = _ => 50,
        };
        var fromOpt = new Option<int>("--from")
        {
            Description = "Pagination offset.",
            DefaultValueFactory = _ => 0,
        };

        var serviceOpt = new Option<string?>("--service")
        {
            Description = "Match traces containing at least one span whose service_name equals this value.",
        };
        var opLikeOpt = new Option<string?>("--op-like")
        {
            Description = "Match traces containing a span whose operation_name LIKEs this SQL pattern (use % as wildcard).",
        };
        var opRegexOpt = new Option<string?>("--op-regex")
        {
            Description = "Match traces containing a span whose operation_name matches this regex (re_match).",
        };
        var opGlobOpt = new Option<string?>("--op-glob")
        {
            Description = "Match traces containing a span whose operation_name matches this glob (* and ?).",
        };
        var statusOpt = new Option<string?>("--status")
        {
            Description = "Match traces containing a span with this span_status (e.g. ERROR, OK, UNSET).",
        };
        var attrOpt = new Option<string[]>("--attr")
        {
            Description = "Match traces containing a span whose attribute <key>=<value>. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var httpStatusOpt = new Option<string?>("--http-status")
        {
            Description = "Match traces containing a span with an HTTP status code in this spec (e.g. 404, 5xx, 4xx,500).",
        };
        var durationGtOpt = new Option<string?>("--duration-gt")
        {
            Description = "Match traces containing a span whose duration exceeds this duration (e.g. 500ms, 2s).",
        };

        var command = new Command("query", "Fetch recent traces with full span trees. Supports filters. Emits a self-describing TraceBundle.");
        command.Add(sinceOpt);
        command.Add(untilOpt);
        command.Add(sizeOpt);
        command.Add(fromOpt);
        command.Add(serviceOpt);
        command.Add(opLikeOpt);
        command.Add(opRegexOpt);
        command.Add(opGlobOpt);
        command.Add(statusOpt);
        command.Add(attrOpt);
        command.Add(httpStatusOpt);
        command.Add(durationGtOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var since = parseResult.GetValue(sinceOpt);
            var until = parseResult.GetValue(untilOpt);
            var size = parseResult.GetValue(sizeOpt);
            var from = parseResult.GetValue(fromOpt);

            var spec = BuildFilterSpec(
                service: parseResult.GetValue(serviceOpt),
                opLike: parseResult.GetValue(opLikeOpt),
                opRegex: parseResult.GetValue(opRegexOpt),
                opGlob: parseResult.GetValue(opGlobOpt),
                status: parseResult.GetValue(statusOpt),
                attrs: parseResult.GetValue(attrOpt) ?? Array.Empty<string>(),
                httpStatus: parseResult.GetValue(httpStatusOpt),
                durationGt: parseResult.GetValue(durationGtOpt));

            var (startUs, endUs) = TimeRangeParser.Resolve(since, until, time, settings.LookbackMinutes);
            var lookbackMinutes = (int)((endUs - startUs) / (60L * 1_000_000L));

            var backend = backendFactory();
            try
            {
                List<string> traceIds;
                Dictionary<string, TraceAggregate> aggregates;
                if (spec.IsEmpty)
                {
                    var latestJson = await backend.GetLatestTracesAsync(startUs, endUs, from, size, ct);
                    (traceIds, aggregates) = ExtractTraceIdsAndAggregates(latestJson);
                }
                else
                {
                    var filteredJson = await backend.SearchFilteredTraceIdsAsync(spec, startUs, endUs, from, size, ct);
                    traceIds = ExtractTraceIds(filteredJson);
                    aggregates = new Dictionary<string, TraceAggregate>(StringComparer.Ordinal);
                }

                IReadOnlyList<TraceInfo> traces;
                if (traceIds.Count == 0)
                {
                    traces = Array.Empty<TraceInfo>();
                }
                else
                {
                    var spansJson = await backend.SearchTraceSpansAsync(traceIds, startUs, endUs, ct);
                    var hits = ExtractHits(spansJson);
                    traces = TraceAssembler.Assemble(hits, aggregates.Count == 0 ? null : aggregates);
                }

                var bundle = BundleBuilder.BuildTraceBundle(
                    command: "query",
                    backend: backend,
                    startTimeUs: startUs,
                    endTimeUs: endUs,
                    lookbackMinutes: lookbackMinutes,
                    queryInfo: new QueryInfo(
                        TraceId: null,
                        RequestedSize: size,
                        From: from,
                        Returned: traces.Count,
                        Filters: FiltersEchoBuilder.From(spec)),
                    traces: traces);

                await stdout.WriteLineAsync(JsonOutput.FormatObject(bundle));
                return 0;
            }
            finally
            {
                (backend as IDisposable)?.Dispose();
            }
        });

        return command;
    }

    public static FilterSpec BuildFilterSpec(
        string? service,
        string? opLike,
        string? opRegex,
        string? opGlob,
        string? status,
        IReadOnlyList<string> attrs,
        string? httpStatus,
        string? durationGt)
    {
        var modeFlags = new[] { opLike, opRegex, opGlob };
        var modeSet = modeFlags.Count(s => s is not null);
        if (modeSet > 1)
            throw new InvalidOperationException(
                "--op-like, --op-regex and --op-glob are mutually exclusive. Pick one.");

        OperationPattern? operation = null;
        if (opLike is not null) operation = new OperationPattern(opLike, PatternMode.Like);
        else if (opRegex is not null) operation = new OperationPattern(opRegex, PatternMode.Regex);
        else if (opGlob is not null) operation = new OperationPattern(opGlob, PatternMode.Glob);

        var attrList = new List<AttributeFilter>();
        foreach (var raw in attrs)
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0)
                throw new InvalidOperationException(
                    $"--attr value '{raw}' must be in key=value form.");
            attrList.Add(new AttributeFilter(raw[..eq], raw[(eq + 1)..]));
        }

        HttpStatusSpec? httpStatusSpec = httpStatus is null ? null : HttpStatusParser.Parse(httpStatus);
        long? durationGtUs = durationGt is null ? null : DurationParser.ParseToMicroseconds(durationGt);

        return new FilterSpec(
            Service: service,
            Operation: operation,
            Status: status,
            Attributes: attrList,
            HttpStatus: httpStatusSpec,
            DurationGtUs: durationGtUs);
    }

    public static List<string> ExtractTraceIds(string searchJson)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(searchJson);
        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return ids;
        foreach (var hit in hits.EnumerateArray())
        {
            if (hit.TryGetProperty("trace_id", out var tid) && tid.ValueKind == JsonValueKind.String)
            {
                var value = tid.GetString();
                if (!string.IsNullOrEmpty(value)) ids.Add(value);
            }
        }
        return ids;
    }

    private static (List<string> TraceIds, Dictionary<string, TraceAggregate> Aggregates) ExtractTraceIdsAndAggregates(string latestJson)
    {
        var traceIds = new List<string>();
        var aggregates = new Dictionary<string, TraceAggregate>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(latestJson);
        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return (traceIds, aggregates);

        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("trace_id", out var tidEl) || tidEl.ValueKind != JsonValueKind.String)
                continue;
            var traceId = tidEl.GetString()!;
            traceIds.Add(traceId);

            string? rootOperation = null, rootService = null;
            if (hit.TryGetProperty("first_event", out var fe) && fe.ValueKind == JsonValueKind.Object)
            {
                if (fe.TryGetProperty("operation_name", out var op) && op.ValueKind == JsonValueKind.String)
                    rootOperation = op.GetString();
                if (fe.TryGetProperty("service_name", out var sv) && sv.ValueKind == JsonValueKind.String)
                    rootService = sv.GetString();
            }

            List<ServiceCount>? services = null;
            if (hit.TryGetProperty("service_name", out var svcs) && svcs.ValueKind == JsonValueKind.Array)
            {
                services = new List<ServiceCount>();
                foreach (var s in svcs.EnumerateArray())
                {
                    var name = s.TryGetProperty("service_name", out var n) ? n.GetString() ?? "" : "";
                    var count = s.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                    services.Add(new ServiceCount(name, count));
                }
            }

            aggregates[traceId] = new TraceAggregate(rootOperation, rootService, services);
        }

        return (traceIds, aggregates);
    }

    public static List<JsonElement> ExtractHits(string searchJson)
    {
        using var doc = JsonDocument.Parse(searchJson);
        var hits = new List<JsonElement>();
        if (!doc.RootElement.TryGetProperty("hits", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return hits;
        foreach (var hit in arr.EnumerateArray())
            hits.Add(hit.Clone());
        return hits;
    }
}
