using OpenTelemetry;
using TestApp.Telemetry;

namespace TestApp.Endpoints;

public static class HealthEndpoints
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public static void MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
        {
            var config = new TelemetryConfiguration();
            configuration.GetSection(TelemetryConfiguration.Key).Bind(config);

            var headersSpecified = config.Headers.IsHeaderSpecified();
            var traces = new
            {
                configured = config.Traces.IsSpecified() && headersSpecified,
                endpoint = config.Traces,
            };
            var logs = new
            {
                configured = config.Logs.IsSpecified() && headersSpecified,
                endpoint = config.Logs,
            };
            var metrics = new
            {
                configured = config.Metrics.IsSpecified() && headersSpecified,
                endpoint = config.Metrics,
            };

            var reachable = await ProbeHostAsync(httpClientFactory, config.Host);
            var connected = reachable && traces.configured && logs.configured && metrics.configured;

            var body = new
            {
                status = connected ? "healthy" : "degraded",
                app = "running",
                telemetry = new
                {
                    host = config.Host,
                    reachable,
                    traces,
                    logs,
                    metrics,
                },
            };

            return Results.Json(body, statusCode: connected ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).WithTags("health");
    }

    private static async Task<bool> ProbeHostAsync(IHttpClientFactory httpClientFactory, string host)
    {
        if (!host.IsSpecified())
        {
            return false;
        }

        try
        {
            using var _ = SuppressInstrumentationScope.Begin();
            using var cts = new CancellationTokenSource(ProbeTimeout);
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, host);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
