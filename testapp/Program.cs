using TestApp.Endpoints;
using TestApp.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

builder.RegisterTelemetry();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    name = "testapp",
    description = "OpenTelemetry signal generator for opentelquery",
    endpoints = new[]
    {
        "/health",
        "POST /shutdown",
        "POST /telemetry/flush?timeoutMs=N",
        "/traces/simple",
        "/traces/ok",
        "/traces/linked",
        "/traces/nested?depth=N",
        "/traces/parallel?count=N",
        "/traces/slow?ms=N",
        "/traces/error?type=invalid|timeout|argument|notfound",
        "/traces/distributed?hops=N",
        "/traces/attributes",
        "/logs/levels",
        "/logs/structured?count=N&user=alice",
        "/logs/exception",
        "/logs/scoped",
        "/logs/correlated",
        "/metrics/counter?inc=N&label=foo",
        "/metrics/updown?delta=N",
        "/metrics/histogram?value=N&op=read",
        "/metrics/gauge?value=N",
        "/scenarios/user-transaction",
        "/scenarios/error-cascade",
    },
}));

app.MapHealthEndpoint();
app.MapShutdownEndpoint();
app.MapTelemetryFlushEndpoint();
app.MapTraceEndpoints();
app.MapLogEndpoints();
app.MapMetricEndpoints();
app.MapScenarioEndpoints();

app.LogTelemetryStatus();

app.Run();
