using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace TestApp.Endpoints;

public static class TelemetryFlushEndpoint
{
    private const int DefaultTimeoutMs = 5_000;

    public static void MapTelemetryFlushEndpoint(this WebApplication app)
    {
        app.MapPost("/telemetry/flush", (
            TracerProvider tracers,
            MeterProvider meters,
            LoggerProvider logs,
            int? timeoutMs) =>
        {
            var timeout = timeoutMs is > 0 ? timeoutMs.Value : DefaultTimeoutMs;

            using var _ = SuppressInstrumentationScope.Begin();

            var tracesFlushed = tracers.ForceFlush(timeout);
            var metricsFlushed = meters.ForceFlush(timeout);
            var logsFlushed = logs.ForceFlush(timeout);

            return Results.Ok(new
            {
                timeoutMs = timeout,
                traces = tracesFlushed,
                metrics = metricsFlushed,
                logs = logsFlushed,
            });
        }).WithTags("telemetry");
    }
}
