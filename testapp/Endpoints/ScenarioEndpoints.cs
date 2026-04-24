using System.Collections.Generic;
using System.Diagnostics;
using TestApp.Telemetry;

namespace TestApp.Endpoints;

public static class ScenarioEndpoints
{
    public static void MapScenarioEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/scenarios").WithTags("scenarios");

        group.MapGet("/user-transaction", async (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Scenarios.UserTransaction");
            using var root = TelemetrySources.Activity.StartActivity("scenarios.user-transaction");
            root?.SetTag("testapp.scenario", "user-transaction");

            logger.LogInformation("User transaction started");

            var sw = Stopwatch.StartNew();
            using (var dbSpan = TelemetrySources.Activity.StartActivity("db.query"))
            {
                dbSpan?.SetTag("db.system", "synthetic");
                dbSpan?.SetTag("db.statement", "SELECT * FROM users WHERE id = @id");
                await Task.Delay(40);
            }

            using (var cacheSpan = TelemetrySources.Activity.StartActivity("cache.lookup"))
            {
                cacheSpan?.SetTag("cache.key", "user:42");
                cacheSpan?.SetTag("cache.hit", true);
                await Task.Delay(15);
            }
            sw.Stop();

            TelemetrySources.RequestsCounter.Add(1,
                new KeyValuePair<string, object?>("scenario", "user-transaction"),
                new KeyValuePair<string, object?>("outcome", "success"));
            TelemetrySources.WorkDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("scenario", "user-transaction"));

            logger.LogInformation("User transaction finished in {ElapsedMs}ms", sw.Elapsed.TotalMilliseconds);
            return Results.Ok(new
            {
                traceId = root?.TraceId.ToString(),
                elapsedMs = sw.Elapsed.TotalMilliseconds,
            });
        });

        group.MapGet("/error-cascade", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TestApp.Scenarios.ErrorCascade");
            using var root = TelemetrySources.Activity.StartActivity("scenarios.error-cascade");
            root?.SetTag("testapp.scenario", "error-cascade");

            try
            {
                ErrorCascadeStep(level: 1, maxDepth: 3);
                return Results.Ok(new { unreachable = true });
            }
            catch (Exception ex)
            {
                root?.AddException(ex);
                logger.LogError(ex, "Error cascade reached top of handler (traceId={TraceId})", root?.TraceId);
                TelemetrySources.RequestsCounter.Add(1,
                    new KeyValuePair<string, object?>("scenario", "error-cascade"),
                    new KeyValuePair<string, object?>("outcome", "error"));
                return Results.Problem(
                    title: "error-cascade",
                    detail: ex.Message,
                    statusCode: 500,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = root?.TraceId.ToString(),
                    });
            }
        });
    }

    private static void ErrorCascadeStep(int level, int maxDepth)
    {
        using var span = TelemetrySources.Activity.StartActivity($"scenarios.error-cascade.level{level}");
        span?.SetTag("testapp.level", level);

        if (level >= maxDepth)
        {
            throw new InvalidOperationException($"error cascade failure at level {level}");
        }

        ErrorCascadeStep(level + 1, maxDepth);
    }
}
