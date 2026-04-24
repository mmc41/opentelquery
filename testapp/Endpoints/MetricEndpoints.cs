using System.Collections.Generic;
using TestApp.Telemetry;

namespace TestApp.Endpoints;

public static class MetricEndpoints
{
    public static void MapMetricEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/metrics").WithTags("metrics");

        group.MapPost("/counter", (long inc = 1, string label = "default") =>
        {
            TelemetrySources.RequestsCounter.Add(inc, new KeyValuePair<string, object?>("label", label));
            return Results.Ok(new { instrument = "testapp.requests.total", inc, label });
        });

        group.MapPost("/updown", (long delta = 1) =>
        {
            TelemetrySources.ActiveOps.Add(delta);
            return Results.Ok(new { instrument = "testapp.active_operations", delta });
        });

        group.MapPost("/histogram", (double value = 12.3, string op = "read") =>
        {
            TelemetrySources.WorkDurationMs.Record(value, new KeyValuePair<string, object?>("op", op));
            return Results.Ok(new { instrument = "testapp.work.duration", value, op });
        });

        group.MapPost("/gauge", (int value = 0) =>
        {
            TelemetrySources.QueueDepth = Math.Max(0, value);
            return Results.Ok(new { instrument = "testapp.queue.depth", value = TelemetrySources.QueueDepth });
        });
    }
}
