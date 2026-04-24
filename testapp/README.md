# testapp

An ASP.NET Core minimal-API app that emits OpenTelemetry signals (traces, logs, metrics) on demand. Used to generate realistic telemetry for the opentelquery project — hit an endpoint, get predictable spans/logs/metrics at an OTLP backend.

## Configuration

Telemetry export is driven by the `Telemetry` section in `appsettings*.json`, plus authentication headers from user secrets. The exporter is plain OTLP/HTTP, so the same shape works against any OpenTelemetry backend that accepts OTLP/HTTP — only the URLs and `Telemetry:Headers` change per vendor.

The `opentelquery` CLI's primary query path is **ClickHouse SQL**, which fans out across SigNoz, Uptrace, HyperDX, qryn, Highlight, and BetterStack (6+ vendors) through a single adapter. SQL is LLM-native (deterministic grammar, abundant training data) and aligns with the CNCF TAG Observability direction of SQL-as-basis for a common query language. It covers traces + logs + metrics in one store, with cross-signal joins on `trace_id`. Pointing testapp at any ClickHouse-SQL backend produces end-to-end data the CLI can query over the same adapter.

`appsettings.Development.json` already points at a local OpenObserve:

```json
"Telemetry": {
  "Host":    "http://localhost:5080",
  "Traces":  "http://localhost:5080/api/default/v1/traces",
  "Logs":    "http://localhost:5080/api/default/v1/logs",
  "Metrics": "http://localhost:5080/api/default/v1/metrics"
}
```

Headers live in user secrets (project id `ea4ccd14-e220-4f9e-8500-36c6706ac965`):

```powershell
dotnet user-secrets set "Telemetry:Headers" "Authorization=Basic <base64(email:password)>, stream-name=default, organization=default" --project testapp
```

Both the endpoint URL and the `Telemetry:Headers` entry must be set for a given signal, otherwise the OTLP exporter for that signal stays inactive. The console exporter is always on, so you can still verify output locally without a backend.

`ServiceName` defaults to `testapp`. An `environment.name` resource attribute is added automatically.

### Pointing testapp at ClickHouse-SQL backends

The ClickHouse-SQL family is the primary query target of the `opentelquery` CLI. All of these accept OTLP/HTTP, so only the `Telemetry` URLs and `Telemetry:Headers` change:

| Backend | Host / OTLP path (default) | Auth header(s) |
| --- | --- | --- |
| **SigNoz** (self-hosted; OTel Collector) | `http://localhost:4318/v1/{signal}` | none for local; `signoz-access-token=<token>` for SigNoz Cloud |
| **Uptrace** (cloud or self-hosted) | `https://api.uptrace.dev/v1/{signal}` | `uptrace-dsn=<dsn>` |
| **HyperDX** | `https://in-otel.hyperdx.io/v1/{signal}` | `authorization=<ingestion-api-key>` |
| **qryn** (self-hosted) | `http://localhost:3100/v1/{signal}` | none for local; `Authorization=Basic <b64>` if proxy-auth'd |
| **Highlight** | `https://otel.highlight.io:4318/v1/{signal}` | `x-highlight-project=<project-id>` |
| **BetterStack** (Telemetry / ClickHouse) | `https://s<source-id>.betterstackdata.com/v1/{signal}` | `Authorization=Bearer <source-token>` |

`{signal}` expands to `traces`, `logs`, `metrics` — set each in `Telemetry:Traces` / `Telemetry:Logs` / `Telemetry:Metrics`. Example (SigNoz self-hosted, no auth required for the local Collector):

```json
"Telemetry": {
  "Host":    "http://localhost:3301",
  "Traces":  "http://localhost:4318/v1/traces",
  "Logs":    "http://localhost:4318/v1/logs",
  "Metrics": "http://localhost:4318/v1/metrics"
}
```

For Cloud tiers that require auth, set the header string the same way as for OpenObserve:

```powershell
dotnet user-secrets set "Telemetry:Headers" "uptrace-dsn=https://TOKEN@api.uptrace.dev?grpc=4317" --project testapp
```

`Telemetry:Host` is only used by `/health` for a reachability probe; pick the backend's root or ingest URL. When a backend doesn't expose a plain root that returns anything useful, leaving `Host` as the OTLP base URL is fine — the probe treats any HTTP response (including 404 / 401) as "reachable".

## Start / stop

The listening URL is configured in `appsettings.json` under the top-level `Urls` key (default `http://localhost:5091`). Change it there if you need a different port; all profiles and plain `dotnet run` will pick it up.

Start (http profile — inherits `Urls` from `appsettings.json`):

```powershell
dotnet run --project testapp --launch-profile http
```

Or the https profile (overrides via `applicationUrl` in `launchSettings.json` to add `https://localhost:7124` alongside the HTTP port):

```powershell
dotnet run --project testapp --launch-profile https
```

Override at runtime without editing files:

```powershell
dotnet run --project testapp --urls "http://localhost:6000"
```

Two clean-shutdown paths, both produce the same unwind: `IHostApplicationLifetime.ApplicationStopping` → OpenTelemetry exporters disposed (pending OTLP batches flushed) → Kestrel drains in-flight requests → process exits 0.

- **Interactive:** press `Ctrl+C` in the console.
- **Scripted / automated:** `curl -X POST http://localhost:5091/shutdown` — returns `202 {"stopping":true}` immediately, then the host tears down.

Avoid force-killing the process (`TaskKill /F`, closing the terminal without Ctrl+C, etc.) — `TerminateProcess` semantics skip exporter disposal, so the final batch of spans/logs/metrics can be lost.

OpenAPI is exposed at `/openapi` in Development. The `/openapi`, `/swagger`, `/health`, and `/shutdown` paths are excluded from trace instrumentation to keep the data clean.

A ready-made request collection is in `testapp.http` (works in VS / Rider / VS Code REST Client).

## Telemetry instruments

Activity source: **`TestApp`** — all custom spans use this source.

Meter: **`TestApp`**:

| Instrument | Type | Unit | Emitted by |
| --- | --- | --- | --- |
| `testapp.requests.total` | Counter&lt;long&gt; | `{request}` | `/metrics/counter`, scenarios |
| `testapp.active_operations` | UpDownCounter&lt;long&gt; | `{op}` | `/metrics/updown` |
| `testapp.work.duration` | Histogram&lt;double&gt; | `ms` | `/metrics/histogram`, user-transaction scenario |
| `testapp.queue.depth` | ObservableGauge&lt;int&gt; | `{item}` | `/metrics/gauge` (sets backing field) |

Auto-instrumentation adds standard ASP.NET Core, HttpClient, runtime, and process metrics + spans.

## Endpoints

All endpoints return JSON. Query/body parameters are clamped to the ranges shown.

### `GET /`
Index page listing all endpoints. Produces the usual ASP.NET Core server span.

### `GET /health`
Liveness + telemetry-backend connectivity check. **Emits no custom traces/logs/metrics**, and the path is excluded from the ASP.NET Core trace filter so no server span is recorded either. The outbound probe against `Telemetry:Host` runs inside an `OpenTelemetry.SuppressInstrumentationScope` so no HttpClient span is produced.

Returns `200 healthy` when the backend root URL responds within 5 s **and** all three signals (traces/logs/metrics) have both an endpoint URL and `Telemetry:Headers` set, otherwise `503 degraded`. Body shape:

```json
{
  "status": "healthy",
  "app": "running",
  "telemetry": {
    "host": "http://localhost:5080",
    "reachable": true,
    "traces":  { "configured": true, "endpoint": "…/v1/traces" },
    "logs":    { "configured": true, "endpoint": "…/v1/logs" },
    "metrics": { "configured": true, "endpoint": "…/v1/metrics" }
  }
}
```

`reachable` means the probe got any HTTP response (including redirects / auth challenges) from `Telemetry:Host` — proof the backend is up, not proof the OTLP endpoints accept writes. `configured` reflects whether the endpoint URL is a valid absolute URI *and* `Telemetry:Headers` is set in user secrets.

### `POST /shutdown`
Requests a graceful shutdown. Calls `IHostApplicationLifetime.StopApplication()`, returns `202 {"stopping":true}` immediately, and the host then unwinds on its normal stop path — OTLP exporters are disposed and flush pending batches before the process exits. The path is filtered out of trace instrumentation (same as `/health`). No request body; any body is ignored. Safe to call multiple times, but once the host has started stopping, subsequent requests race with Kestrel draining and may fail to connect.

### Traces — `/traces/*`

| Endpoint | Query | Emits |
| --- | --- | --- |
| `GET /traces/simple` | — | one `traces.simple` span with tags `testapp.kind`, `testapp.value` and event `work.started` |
| `GET /traces/ok` | — | one `traces.ok` span with `ActivityStatusCode.Ok` set explicitly (`span_status = OK` on ingest). Only endpoint that produces a non-UNSET, non-ERROR span status — use to exercise `opentelquery query --status OK`. |
| `GET /traces/linked` | — | two separate traces: `traces.linked.first` emits and ends, then `traces.linked.second` starts as a new root carrying an `ActivityLink` back to the first's trace/span context (link attribute `testapp.link_reason=follow-up`). Exercises the `links` column / `SpanLink` parsing path, which no other endpoint populates. |
| `GET /traces/nested?depth=N` | `depth` 1–10 (default 3) | parent `traces.nested` + N child spans `traces.nested.level{1..N}` |
| `GET /traces/parallel?count=N` | `count` 1–20 (default 5) | parent `traces.parallel` + N sibling `traces.parallel.child` spans running concurrently |
| `GET /traces/slow?ms=N` | `ms` 0–30000 (default 500) | one `traces.slow` span sleeping N ms |
| `GET /traces/error?type=T` | `type` = `invalid`\|`timeout`\|`argument`\|`notfound` | one `traces.error` span with exception recorded, then throws → **HTTP 500**. ASP.NET server span picks up the error status. |
| `GET /traces/distributed?hops=N` | `hops` 0–5 | `traces.distributed` span; each hop makes an outbound HttpClient call back to this endpoint. Produces N client/server span pairs sharing one trace id. |
| `GET /traces/attributes` | — | one `traces.attributes` span with string/int/long/double/bool/string-array tags and two events (`phase.one`, `phase.two` with its own tag). Useful for validating attribute-type coverage in queries. |

### Logs — `/logs/*`

| Endpoint | Query | Emits |
| --- | --- | --- |
| `GET /logs/levels` | — | six records: Trace, Debug, Information, Warning, Error, Critical (logger `TestApp.Logs.Levels`). Note: Trace/Debug may be filtered by `Logging.LogLevel` config. |
| `GET /logs/structured?count=N&user=U` | `count` 1–100 (default 5), `user` (default `alice`) | N structured info logs with `Index`, `User`, `Timestamp` parameters |
| `GET /logs/exception` | — | one error log with an attached `InvalidOperationException` |
| `GET /logs/scoped` | — | three info logs wrapped in two nested scopes (`correlationId`+`tenant`, then `step`). `IncludeScopes` is enabled, so scope keys land as log attributes. |
| `GET /logs/correlated` | — | one info log emitted inside a `logs.correlated` span — lets you verify trace↔log correlation via `trace_id` / `span_id` on the log record |

### Metrics — `/metrics/*` (POST)

| Endpoint | Query | Effect |
| --- | --- | --- |
| `POST /metrics/counter?inc=N&label=L` | `inc` (default 1), `label` (default `default`) | `testapp.requests.total += inc` with tag `label` |
| `POST /metrics/updown?delta=N` | `delta` (default 1) | `testapp.active_operations += delta` (can be negative) |
| `POST /metrics/histogram?value=V&op=O` | `value` (default 12.3), `op` (default `read`) | record `value` on `testapp.work.duration` with tag `op` |
| `POST /metrics/gauge?value=N` | `value` ≥ 0 (default 0) | set backing field for `testapp.queue.depth` observable gauge |

### Composite scenarios — `/scenarios/*`

| Endpoint | Emits |
| --- | --- |
| `GET /scenarios/user-transaction` | `scenarios.user-transaction` span with child `db.query` and `cache.lookup` spans (DB/cache tags), two info logs, counter increment (`scenario=user-transaction`, `outcome=success`), and a histogram observation for elapsed ms. The full trace/log/metric triple for one happy-path request. |
| `GET /scenarios/error-cascade` | `scenarios.error-cascade` + three nested `scenarios.error-cascade.level{1..3}` spans; the deepest throws. Exception recorded on root span, error log emitted with `traceId`, counter incremented with `outcome=error`. Returns **HTTP 500** ProblemDetails including `traceId`. |

### Telemetry control — `/telemetry/*`

| Endpoint | Query | Effect |
| --- | --- | --- |
| `POST /telemetry/flush?timeoutMs=N` | `timeoutMs` > 0 (default 5000) | Calls `ForceFlush(timeoutMs)` on the registered `TracerProvider`, `MeterProvider`, and `LoggerProvider` in order. Returns `{ timeoutMs, traces, metrics, logs }` where each signal is `true` if the SDK confirmed flush completion within the timeout. Intended for E2E tests: after driving endpoints to generate telemetry, call this, then query the backend — removes the need to sleep-wait for the default OTLP batch export interval. Runs inside `SuppressInstrumentationScope` so the flush call itself produces no spans, and the `/telemetry/*` prefix is filtered out of ASP.NET Core trace instrumentation. |

## Typical testing flow

1. Start OpenObserve (or another OTLP/HTTP backend) and make sure the `Telemetry` endpoints and `Telemetry:Headers` are pointed at it.
2. `dotnet run --project testapp --launch-profile http`
3. Confirm wiring with `curl http://localhost:5091/health` — expect `status: healthy`.
4. Hit endpoints from `testapp.http` or curl to generate the signals you need.
5. `curl -X POST http://localhost:5091/telemetry/flush` to push pending OTLP batches immediately — useful in E2E tests so you don't have to wait out the default export interval before querying.
6. Query the backend for streams `default` (traces/logs) and `testapp_*` (metrics) filtered by `service_name = testapp`.
7. `Ctrl+C` (interactive) or `curl -X POST http://localhost:5091/shutdown` (scripted) to stop.

For a quick smoke test of everything, walk through `testapp.http` top to bottom — expect 200s except `/traces/error` and `/scenarios/error-cascade`, which intentionally return 500, `/telemetry/flush` which returns 200 with per-signal booleans, and `/shutdown` which returns 202 and ends the run.
