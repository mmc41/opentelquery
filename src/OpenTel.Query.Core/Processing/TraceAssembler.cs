using System.Text.Json;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Processing;

public static class TraceAssembler
{
    private static readonly HashSet<string> StructuralKeys = new(StringComparer.Ordinal)
    {
        "_timestamp", "trace_id", "span_id",
        "reference_parent_span_id", "reference_parent_trace_id", "reference_ref_type",
        "operation_name", "start_time", "end_time", "duration",
        "span_kind", "span_status", "flags",
        "events", "links",
    };

    public static IReadOnlyList<TraceInfo> Assemble(
        IEnumerable<JsonElement> spanHits,
        IReadOnlyDictionary<string, TraceAggregate>? aggregates = null)
    {
        var parsed = spanHits.Select(ParseSpan).ToList();
        return parsed
            .GroupBy(s => s.TraceId, StringComparer.Ordinal)
            .Select(group => BuildTraceInfo(group.Key, group.ToList(), aggregates))
            .OrderByDescending(t => t.StartTime)
            .ToList();
    }

    private static TraceInfo BuildTraceInfo(
        string traceId,
        List<ParsedSpan> spans,
        IReadOnlyDictionary<string, TraceAggregate>? aggregates)
    {
        var traceStartNs = spans.Min(s => s.StartTimeNs);
        var traceEndNs = spans.Max(s => s.EndTimeNs);

        var bySpanId = spans.ToDictionary(s => s.SpanId, StringComparer.Ordinal);
        var childrenByParent = spans
            .Where(s => !string.IsNullOrEmpty(s.ParentSpanId) && bySpanId.ContainsKey(s.ParentSpanId!))
            .GroupBy(s => s.ParentSpanId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var roots = spans
            .Where(s => string.IsNullOrEmpty(s.ParentSpanId) || !bySpanId.ContainsKey(s.ParentSpanId!))
            .OrderBy(s => s.StartTimeNs)
            .ToList();

        var rootNodes = roots
            .Select(r => BuildNode(r, childrenByParent, traceStartNs))
            .OrderBy(n => n.StartOffsetMs)
            .ToList();

        var firstRoot = roots.FirstOrDefault();
        TraceAggregate? aggregate = null;
        aggregates?.TryGetValue(traceId, out aggregate);

        var rootOperation = aggregate?.RootOperation ?? firstRoot?.Operation ?? "";
        var rootService = aggregate?.RootService ?? firstRoot?.Service ?? "";
        var services = aggregate?.Services ?? DeriveServiceCounts(spans);

        return new TraceInfo(
            TraceId: traceId,
            RootOperation: rootOperation,
            RootService: rootService,
            SpanCount: spans.Count,
            ErrorCount: spans.Count(s => s.Status == "ERROR"),
            StartTime: FromUnixNanos(traceStartNs),
            EndTime: FromUnixNanos(traceEndNs),
            DurationMs: Math.Round((traceEndNs - traceStartNs) / 1_000_000.0, 2),
            Services: services,
            RootSpans: rootNodes);
    }

    private static SpanNode BuildNode(
        ParsedSpan span,
        Dictionary<string, List<ParsedSpan>> childrenByParent,
        long traceStartNs)
    {
        var children = childrenByParent.TryGetValue(span.SpanId, out var kids)
            ? kids.Select(c => BuildNode(c, childrenByParent, traceStartNs))
                  .OrderBy(n => n.StartOffsetMs)
                  .ToList()
            : (IReadOnlyList<SpanNode>)Array.Empty<SpanNode>();

        return new SpanNode(
            SpanId: span.SpanId,
            ParentSpanId: string.IsNullOrEmpty(span.ParentSpanId) ? null : span.ParentSpanId,
            Operation: span.Operation,
            Service: span.Service,
            Kind: span.Kind,
            KindCode: span.KindCode,
            Status: span.Status,
            StartTime: FromUnixNanos(span.StartTimeNs),
            StartOffsetMs: Math.Round((span.StartTimeNs - traceStartNs) / 1_000_000.0, 2),
            DurationMs: Math.Round(span.DurationUs / 1_000.0, 2),
            EndTime: FromUnixNanos(span.EndTimeNs),
            Flags: span.Flags,
            Process: span.Process,
            Attributes: span.Attributes,
            Events: span.Events,
            Exceptions: span.Exceptions,
            Links: span.Links,
            Children: children);
    }

    private static IReadOnlyList<ServiceCount> DeriveServiceCounts(List<ParsedSpan> spans) =>
        spans.GroupBy(s => s.Service, StringComparer.Ordinal)
             .Select(g => new ServiceCount(g.Key, g.Count()))
             .OrderByDescending(sc => sc.SpanCount)
             .ToList();

    private static ParsedSpan ParseSpan(JsonElement hit)
    {
        var process = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        string? traceId = null, spanId = null, parentSpanId = null;
        string operation = "", service = "", status = "UNSET";
        long startTimeNs = 0, endTimeNs = 0, durationUs = 0;
        int flags = 0;
        int kindCode = 0;
        string eventsRaw = "[]", linksRaw = "[]";

        foreach (var property in hit.EnumerateObject())
        {
            var key = property.Name;
            var value = property.Value;

            switch (key)
            {
                case "trace_id": traceId = value.GetString(); break;
                case "span_id": spanId = value.GetString(); break;
                case "reference_parent_span_id": parentSpanId = value.GetString(); break;
                case "operation_name": operation = value.GetString() ?? ""; break;
                case "service_name":
                    service = value.GetString() ?? "";
                    process[key] = service;
                    break;
                case "start_time": startTimeNs = ReadInt64(value); break;
                case "end_time": endTimeNs = ReadInt64(value); break;
                case "duration": durationUs = ReadInt64(value); break;
                case "span_kind": kindCode = ReadInt32String(value); break;
                case "span_status": status = value.GetString() ?? "UNSET"; break;
                case "flags": flags = ReadInt32(value); break;
                case "events": eventsRaw = value.GetString() ?? "[]"; break;
                case "links": linksRaw = value.GetString() ?? "[]"; break;
                default:
                    if (StructuralKeys.Contains(key)) break;
                    if (key.StartsWith("service_", StringComparison.Ordinal))
                        process[key] = value.ValueKind == JsonValueKind.String
                            ? value.GetString() ?? ""
                            : value.GetRawText();
                    else
                        attributes[key] = value.Clone();
                    break;
            }
        }

        if (traceId is null || spanId is null)
            throw new InvalidOperationException("Span hit is missing trace_id or span_id.");

        var (events, exceptions) = ParseEvents(eventsRaw, startTimeNs);
        var links = ParseLinks(linksRaw);

        return new ParsedSpan(
            TraceId: traceId,
            SpanId: spanId,
            ParentSpanId: parentSpanId,
            Operation: operation,
            Service: service,
            KindCode: kindCode,
            Kind: DecodeKind(kindCode),
            Status: status,
            StartTimeNs: startTimeNs,
            EndTimeNs: endTimeNs,
            DurationUs: durationUs,
            Flags: flags,
            Process: process,
            Attributes: attributes,
            Events: events,
            Exceptions: exceptions,
            Links: links);
    }

    private static (IReadOnlyList<SpanEvent> Events, IReadOnlyList<SpanException> Exceptions) ParseEvents(string raw, long spanStartNs)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
            return (Array.Empty<SpanEvent>(), Array.Empty<SpanException>());

        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { return (Array.Empty<SpanEvent>(), Array.Empty<SpanException>()); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (Array.Empty<SpanEvent>(), Array.Empty<SpanException>());

            var events = new List<SpanEvent>();
            var exceptions = new List<SpanException>();

            foreach (var ev in doc.RootElement.EnumerateArray())
            {
                var name = ev.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var timeNs = ReadEventTimeNs(ev);
                var attrs = ReadEventAttributes(ev);
                var offsetMs = Math.Round((timeNs - spanStartNs) / 1_000_000.0, 2);
                var time = FromUnixNanos(timeNs);

                if (name == "exception")
                {
                    exceptions.Add(new SpanException(
                        Time: time,
                        TimeOffsetMs: offsetMs,
                        Type: GetExceptionAttr(attrs, "exception.type", "exception_type"),
                        Message: GetExceptionAttr(attrs, "exception.message", "exception_message"),
                        Stacktrace: GetExceptionAttr(attrs, "exception.stacktrace", "exception_stacktrace")));
                }
                else
                {
                    events.Add(new SpanEvent(
                        Time: time,
                        TimeOffsetMs: offsetMs,
                        Name: name,
                        Attributes: attrs));
                }
            }

            return (events, exceptions);
        }
    }

    private static long ReadEventTimeNs(JsonElement ev)
    {
        if (ev.TryGetProperty("time_unix_nano", out var tns) && tns.ValueKind == JsonValueKind.Number)
            return tns.GetInt64();
        if (ev.TryGetProperty("_timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number)
            return ts.GetInt64() * 1000L;
        return 0;
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadEventAttributes(JsonElement ev)
    {
        if (!ev.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement>();
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in attrs.EnumerateObject())
            result[p.Name] = p.Value.Clone();
        return result;
    }

    private static string? GetExceptionAttr(IReadOnlyDictionary<string, JsonElement> attrs, params string[] keys)
    {
        foreach (var key in keys)
            if (attrs.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static IReadOnlyList<SpanLink> ParseLinks(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
            return Array.Empty<SpanLink>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { return Array.Empty<SpanLink>(); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<SpanLink>();

            var links = new List<SpanLink>();
            foreach (var lk in doc.RootElement.EnumerateArray())
            {
                var tid = lk.TryGetProperty("trace_id", out var t) ? t.GetString() ?? "" : "";
                var sid = lk.TryGetProperty("span_id", out var s) ? s.GetString() ?? "" : "";
                var attrs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (lk.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object)
                    foreach (var p in a.EnumerateObject())
                        attrs[p.Name] = p.Value.Clone();
                links.Add(new SpanLink(tid, sid, attrs));
            }
            return links;
        }
    }

    private static string DecodeKind(int code) => code switch
    {
        0 => "UNSPECIFIED",
        1 => "INTERNAL",
        2 => "SERVER",
        3 => "CLIENT",
        4 => "PRODUCER",
        5 => "CONSUMER",
        _ => "UNKNOWN",
    };

    private static long ReadInt64(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetInt64(),
        JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
        _ => 0,
    };

    private static int ReadInt32(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetInt32(),
        JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
        _ => 0,
    };

    private static int ReadInt32String(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
        JsonValueKind.Number => v.GetInt32(),
        _ => 0,
    };

    private static DateTimeOffset FromUnixNanos(long ns) =>
        DateTimeOffset.UnixEpoch.AddTicks(ns / 100);

    private sealed record ParsedSpan(
        string TraceId,
        string SpanId,
        string? ParentSpanId,
        string Operation,
        string Service,
        int KindCode,
        string Kind,
        string Status,
        long StartTimeNs,
        long EndTimeNs,
        long DurationUs,
        int Flags,
        IReadOnlyDictionary<string, string> Process,
        IReadOnlyDictionary<string, JsonElement> Attributes,
        IReadOnlyList<SpanEvent> Events,
        IReadOnlyList<SpanException> Exceptions,
        IReadOnlyList<SpanLink> Links);
}
