using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TestApp.Telemetry;

public static class SetupTelemetry
{
    public static TelemetryConfiguration RegisterTelemetry(this IHostApplicationBuilder builder)
    {
        var config = new TelemetryConfiguration();
        builder.Configuration.GetSection(TelemetryConfiguration.Key).Bind(config);

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(string.IsNullOrWhiteSpace(config.ServiceName) ? "testapp" : config.ServiceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["environment.name"] = builder.Environment.EnvironmentName,
            });

        builder.Services.AddOpenTelemetry()
            .WithLogging(logging =>
            {
                logging
                    .SetResourceBuilder(resourceBuilder)
                    .AddConsoleExporter();

                if (config.Logs.IsSpecified() && config.Headers.IsHeaderSpecified())
                {
                    logging.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = config.Logs.ToValidUri("Telemetry:Logs");
                        opts.Headers = config.Headers;
                        opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            },
            loggingOpts =>
            {
                loggingOpts.IncludeFormattedMessage = true;
                loggingOpts.IncludeScopes = true;
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .SetErrorStatusOnException(true)
                    .AddSource(TelemetrySources.AppActivitySourceName)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = httpContext =>
                        {
                            var path = httpContext.Request.Path.Value;
                            return path == null
                                || (!path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                                    && !path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                                    && !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                                    && !path.StartsWith("/shutdown", StringComparison.OrdinalIgnoreCase)
                                    && !path.StartsWith("/telemetry/", StringComparison.OrdinalIgnoreCase));
                        };
                    })
                    .AddHttpClientInstrumentation(o => o.RecordException = true)
                    .AddConsoleExporter();

                if (config.Traces.IsSpecified() && config.Headers.IsHeaderSpecified())
                {
                    tracing.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = config.Traces.ToValidUri("Telemetry:Traces");
                        opts.Headers = config.Headers;
                        opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter(TelemetrySources.AppMeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddConsoleExporter((_, readerOptions) =>
                    {
                        readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
                    });

                if (config.Metrics.IsSpecified() && config.Headers.IsHeaderSpecified())
                {
                    metrics.AddOtlpExporter((opts, readerOptions) =>
                    {
                        opts.Endpoint = config.Metrics.ToValidUri("Telemetry:Metrics");
                        opts.Headers = config.Headers;
                        opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                        readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
                    });
                }
            });

        return config;
    }

    public static void LogTelemetryStatus(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var config = new TelemetryConfiguration();
        app.Configuration.GetSection(TelemetryConfiguration.Key).Bind(config);

        LogSignalStatus(logger, "Traces", config.Traces, config.Headers, config.Host);
        LogSignalStatus(logger, "Metrics", config.Metrics, config.Headers, config.Host);
        LogSignalStatus(logger, "Logs", config.Logs, config.Headers, config.Host);
    }

    private static void LogSignalStatus(ILogger logger, string signal, string endpoint, string headers, string host)
    {
        if (endpoint.IsSpecified() && headers.IsHeaderSpecified())
        {
            logger.LogInformation("OpenTelemetry {Signal} OTLP export active (Host={Host})", signal, host);
        }
        else
        {
            logger.LogWarning(
                "OpenTelemetry {Signal} OTLP export inactive (set Telemetry:{Signal} and Telemetry:Headers to enable). Console exporter still active.",
                signal, signal);
        }
    }
}
