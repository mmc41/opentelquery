using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TestApp.Telemetry;

public static class TelemetrySources
{
    public const string AppActivitySourceName = "TestApp";
    public const string AppMeterName = "TestApp";

    public static readonly ActivitySource Activity = new(AppActivitySourceName);
    public static readonly Meter Meter = new(AppMeterName);

    public static readonly Counter<long> RequestsCounter =
        Meter.CreateCounter<long>("testapp.requests.total", unit: "{request}");

    public static readonly UpDownCounter<long> ActiveOps =
        Meter.CreateUpDownCounter<long>("testapp.active_operations", unit: "{op}");

    public static readonly Histogram<double> WorkDurationMs =
        Meter.CreateHistogram<double>("testapp.work.duration", unit: "ms");

    private static int _queueDepth;
    public static int QueueDepth
    {
        get => Volatile.Read(ref _queueDepth);
        set => Volatile.Write(ref _queueDepth, value);
    }

    static TelemetrySources()
    {
        Meter.CreateObservableGauge(
            "testapp.queue.depth",
            () => Volatile.Read(ref _queueDepth),
            unit: "{item}");
    }
}
