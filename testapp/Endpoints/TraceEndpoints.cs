using System.Diagnostics;
using TestApp.Telemetry;

namespace TestApp.Endpoints;

public static class TraceEndpoints
{
    private const int MaxDepth = 10;
    private const int MaxParallel = 20;
    private const int MaxHops = 5;

    public static void MapTraceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/traces").WithTags("traces");

        group.MapGet("/simple", () =>
        {
            using var activity = TelemetrySources.Activity.StartActivity("traces.simple");
            activity?.SetTag("testapp.kind", "simple");
            activity?.SetTag("testapp.value", 42);
            activity?.AddEvent(new ActivityEvent("work.started"));
            return Results.Ok(new { traceId = activity?.TraceId.ToString(), spanId = activity?.SpanId.ToString() });
        });

        group.MapGet("/ok", () =>
        {
            using var activity = TelemetrySources.Activity.StartActivity("traces.ok");
            activity?.SetTag("testapp.kind", "ok");
            activity?.SetStatus(ActivityStatusCode.Ok, description: "explicit OK status");
            return Results.Ok(new
            {
                traceId = activity?.TraceId.ToString(),
                spanId = activity?.SpanId.ToString(),
                status = "OK",
            });
        });

        group.MapGet("/linked", () =>
        {
            var result = EmitLinkedSpans(TelemetrySources.Activity);
            return Results.Ok(new
            {
                firstTraceId = result.First.TraceId.ToString(),
                firstSpanId = result.First.SpanId.ToString(),
                secondTraceId = result.Second.TraceId.ToString(),
                secondSpanId = result.Second.SpanId.ToString(),
            });
        });

        group.MapGet("/nested", (int depth = 3) =>
        {
            depth = Math.Clamp(depth, 1, MaxDepth);
            using var root = TelemetrySources.Activity.StartActivity("traces.nested");
            root?.SetTag("testapp.depth", depth);
            DoNested(depth, 1);
            return Results.Ok(new { traceId = root?.TraceId.ToString(), depth });
        });

        group.MapGet("/parallel", async (int count = 5) =>
        {
            count = Math.Clamp(count, 1, MaxParallel);
            using var parent = TelemetrySources.Activity.StartActivity("traces.parallel");
            parent?.SetTag("testapp.count", count);
            var parentContext = Activity.Current?.Context ?? default;

            var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
            {
                using var child = TelemetrySources.Activity.StartActivity(
                    $"traces.parallel.child",
                    ActivityKind.Internal,
                    parentContext);
                child?.SetTag("testapp.index", i);
                await Task.Delay(Random.Shared.Next(10, 80));
            })).ToArray();

            await Task.WhenAll(tasks);
            return Results.Ok(new { traceId = parent?.TraceId.ToString(), count });
        });

        group.MapGet("/slow", async (int ms = 500) =>
        {
            ms = Math.Clamp(ms, 0, 30_000);
            using var activity = TelemetrySources.Activity.StartActivity("traces.slow");
            activity?.SetTag("testapp.delay_ms", ms);
            await Task.Delay(ms);
            return Results.Ok(new { traceId = activity?.TraceId.ToString(), ms });
        });

        group.MapGet("/error", (string type = "invalid") =>
        {
            using var activity = TelemetrySources.Activity.StartActivity("traces.error");
            activity?.SetTag("testapp.error_type", type);
            Exception ex = type.ToLowerInvariant() switch
            {
                "timeout" => new TimeoutException("Simulated timeout from /traces/error"),
                "argument" => new ArgumentException("Simulated argument error from /traces/error"),
                "notfound" => new KeyNotFoundException("Simulated not-found from /traces/error"),
                _ => new InvalidOperationException("Simulated invalid operation from /traces/error"),
            };
            activity?.AddException(ex);
            throw ex;
        });

        group.MapGet("/distributed", async (int hops, IHttpClientFactory httpClientFactory, HttpContext ctx) =>
        {
            var remaining = Math.Clamp(hops, 0, MaxHops);
            using var activity = TelemetrySources.Activity.StartActivity("traces.distributed");
            activity?.SetTag("testapp.hops_remaining", remaining);

            if (remaining == 0)
            {
                return Results.Ok(new { traceId = activity?.TraceId.ToString(), terminal = true });
            }

            var baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var client = httpClientFactory.CreateClient();
            var nextUrl = $"{baseUri}/traces/distributed?hops={remaining - 1}";
            var response = await client.GetAsync(nextUrl);
            var body = await response.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json");
        });

        group.MapGet("/attributes", () =>
        {
            using var activity = TelemetrySources.Activity.StartActivity("traces.attributes");
            activity?.SetTag("attr.string", "hello");
            activity?.SetTag("attr.int", 7);
            activity?.SetTag("attr.long", 9_999_999_999L);
            activity?.SetTag("attr.double", 3.14159);
            activity?.SetTag("attr.bool", true);
            activity?.SetTag("attr.string_array", new[] { "a", "b", "c" });
            activity?.AddEvent(new ActivityEvent("phase.one"));
            activity?.AddEvent(new ActivityEvent("phase.two", tags: new ActivityTagsCollection
            {
                ["phase.detail"] = "extra",
            }));
            return Results.Ok(new { traceId = activity?.TraceId.ToString() });
        });
    }

    private static void DoNested(int depth, int level)
    {
        if (level > depth)
        {
            return;
        }

        using var child = TelemetrySources.Activity.StartActivity($"traces.nested.level{level}");
        child?.SetTag("testapp.level", level);
        DoNested(depth, level + 1);
    }

    public readonly record struct LinkedSpansResult(
        ActivityContext First,
        ActivityContext Second,
        ActivityLink[] SecondLinks);

    public static LinkedSpansResult EmitLinkedSpans(ActivitySource source)
    {
        ActivityContext firstContext;
        using (var first = source.StartActivity("traces.linked.first"))
        {
            first?.SetTag("testapp.linked_role", "first");
            firstContext = first?.Context ?? default;
        }

        var link = new ActivityLink(firstContext, new ActivityTagsCollection
        {
            ["testapp.link_reason"] = "follow-up",
        });

        // ActivitySource.StartActivity(..., default(ActivityContext), ...) falls back to
        // Activity.Current when no explicit parent context is provided, so to produce a
        // genuinely separate root trace we must detach the ambient activity for the call.
        var savedCurrent = Activity.Current;
        Activity.Current = null;
        try
        {
            using var second = source.StartActivity(
                "traces.linked.second",
                ActivityKind.Internal,
                default(ActivityContext),
                tags: null,
                links: new[] { link });
            second?.SetTag("testapp.linked_role", "second");

            return new LinkedSpansResult(
                firstContext,
                second?.Context ?? default,
                second?.Links.ToArray() ?? Array.Empty<ActivityLink>());
        }
        finally
        {
            Activity.Current = savedCurrent;
        }
    }
}
