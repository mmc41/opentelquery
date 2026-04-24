namespace TestApp.Telemetry;

public sealed record TelemetryConfiguration
{
    public const string Key = "Telemetry";

    public string Host { get; init; } = string.Empty;
    public string Traces { get; init; } = string.Empty;
    public string Logs { get; init; } = string.Empty;
    public string Metrics { get; init; } = string.Empty;
    public string Headers { get; init; } = string.Empty;
    public string ServiceName { get; init; } = "testapp";
}
