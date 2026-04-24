using System.Diagnostics;
using TestApp.Telemetry;

namespace TestApp.Endpoints;

public static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/logs").WithTags("logs");

        group.MapGet("/levels", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Logs.Levels");
            logger.LogTrace("trace-level message {Index}", 1);
            logger.LogDebug("debug-level message {Index}", 2);
            logger.LogInformation("information-level message {Index}", 3);
            logger.LogWarning("warning-level message {Index}", 4);
            logger.LogError("error-level message {Index}", 5);
            logger.LogCritical("critical-level message {Index}", 6);
            return Results.Ok(new { emitted = 6 });
        });

        group.MapGet("/structured", (ILoggerFactory loggerFactory, int count = 5, string user = "alice") =>
        {
            count = Math.Clamp(count, 1, 100);
            var logger = loggerFactory.CreateLogger("TestApp.Logs.Structured");
            for (var i = 0; i < count; i++)
            {
                logger.LogInformation(
                    "Structured event {Index} for user {User} at {Timestamp:O}",
                    i, user, DateTimeOffset.UtcNow);
            }
            return Results.Ok(new { count, user });
        });

        group.MapGet("/exception", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Logs.Exception");
            try
            {
                throw new InvalidOperationException("Simulated failure for /logs/exception");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Caught exception while processing {Operation}", "demo");
            }
            return Results.Ok(new { logged = true });
        });

        group.MapGet("/scoped", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Logs.Scoped");
            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["correlationId"] = Guid.NewGuid().ToString("N"),
                ["tenant"] = "acme",
            }))
            {
                logger.LogInformation("Inside scope: starting work");
                using (logger.BeginScope(new Dictionary<string, object> { ["step"] = "validate" }))
                {
                    logger.LogInformation("Inside nested scope: validating");
                }
                logger.LogInformation("Inside scope: finished work");
            }
            return Results.Ok(new { scoped = true });
        });

        group.MapGet("/correlated", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Logs.Correlated");
            using var activity = TelemetrySources.Activity.StartActivity("logs.correlated");
            logger.LogInformation("Correlated log inside activity {ActivityName}", activity?.DisplayName);
            return Results.Ok(new
            {
                traceId = activity?.TraceId.ToString(),
                spanId = activity?.SpanId.ToString(),
            });
        });
    }
}
