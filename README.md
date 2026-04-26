<div align="center">

# OpenTel.Query

**Vendor-neutral .NET CLI for querying OpenTelemetry-compatible observability backends — emits self-describing JSON bundles for shell operators and LLM agents.**

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Status: early access](https://img.shields.io/badge/status-early%20access-orange.svg)](#project-status)

</div>

OpenTel.Query is a single-binary command-line tool that gives backend engineers, SREs, and LLM agents one query vocabulary across multiple observability backends. The same `query --service Api --status ERROR --since "2h ago"` invocation produces the same kind of answer whether the data sits in OpenObserve SQL, Tempo TraceQL, or Jaeger's HTTP API. Every response is a self-describing JSON bundle that an agent can interpret without vendor documentation.

## Why OpenTel.Query?

Debugging a live incident with telemetry today usually means juggling three frictions: each backend speaks its own dialect (OpenObserve SQL, TraceQL, LogQL, Jaeger REST-with-tags), responses are vendor-shaped and drop the filter context the caller asked for, and there is no first-class drill-down path that walks from a symptom to a span tree to surrounding logs across both traces and logs. The result is lost minutes per incident — and LLM agents that cannot reliably debug beyond the single backend they were trained on. OpenTel.Query collapses all three problems into one CLI surface.

- **One filter vocabulary across backends** — `--service`, `--op-like`/`--op-regex`/`--op-glob`, `--status`, `--attr k=v`, `--http-status`, `--duration-gt`, `--since`/`--until`. Adapters translate to the backend's native query language.
- **LLM-first output shape** — every bundle carries `$schema`, `source.backend`, `source.properties`, `window`, and `query_info.filters` echoing back what the caller sent. Schemas are backend-independent.
- **Correlated drill-down in four invocations** — `query` (find the trace) → `lookup` (full span tree) → `logs` (records by trace id) → `around` (context window at a timestamp). One muscle memory; no UI hopping.
- **Local, credentialled, no cloud copilot** — runs against your existing backend with `Telemetry:Headers` from user secrets or environment variables. No vendor account required.

## Features

- Filtered trace query, single-trace lookup, log search, and time-anchored "around a timestamp" context windows.
- Stream and schema discovery (`streams`, `schema <name> --type`) so callers can learn what fields exist before writing a filter.
- Self-describing JSON bundles (`opentel-query-trace/v1`, `opentel-query-log/v1`, `opentel-query-streams/v1`, `opentel-query-schema/v1`) on stdout; errors on stderr with non-zero exit codes.
- Relative + absolute time vocabulary (`--since "2h ago"`, `--until 2026-04-20T14:00:00Z`).
- Pluggable backend adapters via `ITelemetryBackend`. Reference adapter: OpenObserve.
- Layered configuration (`appsettings.json` → user secrets → environment variables); secrets never leave the secret store.

## Quickstart

```bash
# Build the solution
dotnet build opentelquery.sln

# Drop your OTLP headers into user secrets (one-time, per the OpenObserve adapter)
dotnet user-secrets set "Telemetry:Headers" \
  "Authorization=Basic <base64(email:password)>, organization=default, stream-name=default" \
  --project src/OpenTel.Query.Cli

# Show the full CLI surface (one shell call, recursive --help)
dotnet run --project src/OpenTel.Query.Cli -- --help

# Find error traces in the Api service over the last 15 minutes
dotnet run --project src/OpenTel.Query.Cli -- query \
  --service Api --status ERROR --since "15m ago"
```

Each command writes a JSON bundle to stdout. The bundle's `query_info.filters` echoes back the parsed filter spec, so you (or an agent) can confirm what was actually sent to the backend.

## Installation

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An OpenTelemetry-compatible backend reachable from your machine. The reference adapter targets [OpenObserve](https://openobserve.ai/); a local instance can be spun up with `scripts/setup_dev_local_openobserve.ps1` and `scripts/run_dev_local_openobserve.ps1`.

### Build from source

A NuGet `dotnet tool` package is on the roadmap; for now build from source:

```bash
git clone https://github.com/mmc41/opentelquery.git
cd opentelquery
dotnet build opentelquery.sln
```

The CLI is `src/OpenTel.Query.Cli`. Run it via `dotnet run --project src/OpenTel.Query.Cli -- <subcommand>` or invoke the produced executable from `src/OpenTel.Query.Cli/bin/`.

## Usage

The CLI exposes six subcommands. All accept `--since`/`--until` (relative or absolute), all return self-describing bundles, and all write JSON to stdout.

```bash
# 1. Filtered trace query — find candidate traces from a symptom
dotnet run --project src/OpenTel.Query.Cli -- query \
  --service Api --op-like "POST /orders%" --status ERROR --duration-gt 500ms \
  --since "1h ago" --size 50

# 2. Single-trace lookup — pull the full span tree for a known trace id
dotnet run --project src/OpenTel.Query.Cli -- lookup 9d2f...c01a --since "2h ago"

# 3. Log search — correlate logs to a trace, or filter by service/level
dotnet run --project src/OpenTel.Query.Cli -- logs \
  --trace-id 9d2f...c01a --level Warning --match-field body --match-like "%timeout%"

# 4. Context around a timestamp — what else was happening at the incident moment
dotnet run --project src/OpenTel.Query.Cli -- around \
  --at 2026-04-20T14:03:17Z --stream default --stream-type logs --size 100
```

```bash
# 5. Stream discovery — list streams in the configured organization
dotnet run --project src/OpenTel.Query.Cli -- streams --type logs --fetch-schema

# 6. Schema introspection — fields and types for a single stream
dotnet run --project src/OpenTel.Query.Cli -- schema default --type logs
```

The four-step drill-down (`query` → `lookup` → `logs` → `around`) is the canonical incident-triage path. Each step accepts the output of the previous step (trace id, timestamp) as input.

## Configuration

Configuration is layered; later sources override earlier ones:

| Source | Purpose | Keys |
|--------|---------|------|
| `src/OpenTel.Query.Cli/appsettings.json` | Defaults, non-secret | `Backend` (default `openobserve`), `Query:LookbackMinutes` (default `400`), `UserSecretsId` |
| User secrets | Credentials | `Telemetry:Headers` — OTLP header string with `Authorization`, `organization`, `stream-name` |
| Environment variables | Per-invocation override | `Backend`, `Telemetry__Headers`, `OpenObserve__Host`, `Query__LookbackMinutes`, etc. |

Switching backends is one config change: set `Backend` to the adapter's identifier. The OpenObserve adapter additionally reads `OpenObserve:Host`.

## Architecture

The solution is layered so adapters own the query language and commands stay backend-agnostic.

```
OpenTel.Query.Cli (exe)
  │  composes everything at startup
  │  commands translate CLI args → FilterSpec/LogsFilterSpec → backend call → bundle → stdout JSON
  ▼
OpenTel.Query.Core (library)
  │  ITelemetryBackend — adapter contract (Abstractions/)
  │  FilterSpec / LogsFilterSpec — vendor-neutral filter models (Filtering/)
  │  Trace/Log/Streams Assembler — parse raw backend JSON into bundle models (Processing/)
  │  Bundle types — TraceBundle / LogBundle / StreamsBundle / SchemaBundle (Model/)
  ▼
OpenTel.Query.Backends.OpenObserve (library)
     implements ITelemetryBackend; owns SQL generation (OpenObserveSqlTranslator)
     and HTTP shape against OpenObserve's REST API
```

Adapters take `FilterSpec`/`LogsFilterSpec` (domain objects), not SQL strings. Commands never construct backend queries. Backend selection is one `switch` in `src/OpenTel.Query.Cli/BackendFactory.cs` — adding a new backend means a new project, one switch case, and one project reference. See [`prd.md`](prd.md) and [`CLAUDE.md`](CLAUDE.md) for the full design rationale and the seam for adding a new adapter.

## Testing

```bash
# Run the entire test suite (xUnit v3 across four projects)
dotnet test opentelquery.sln

# Run a single test class or method
dotnet test test/OpenTel.Query.Cli.UnitTests \
  --filter "FullyQualifiedName~CliBuilderTests"
```

The unit suite covers filter SQL generation, time/duration parsing, bundle serialization, and CLI wiring (against a fake `ITelemetryBackend`). The OpenObserve adapter tests use a `RecordingHandler` to assert the outbound HTTP shape — mirror that pattern when adding a new backend.

A companion `testapp/` project (ASP.NET Core minimal API) emits OpenTelemetry signals on demand for end-to-end runs against a live backend; see [`testapp/README.md`](testapp/README.md).

## Known Limitations

- **One backend in tree.** OpenObserve is the only adapter shipped today. Tempo, Jaeger, Loki, Datadog, and ClickHouse-SQL adapters are designed-for but not yet implemented.
- **Traces and logs only.** Metrics and PromQL are out of scope for the current phase.
- **Single backend per invocation.** The CLI does not federate queries across multiple backends.
- **Not a backend.** OpenTel.Query does not ingest, store, index, or alert on telemetry; it is a query-side tool sitting in front of someone else's storage.
- **Distributed as source.** A `dotnet tool` package is planned but not yet published.

## Project Status

Early access. The OpenObserve adapter and all six CLI commands are functional and exercised by 140+ unit tests across the four test projects. The public surface (CLI flags, bundle schemas) is intended to remain stable but may shift on a dot release while the second adapter lands. Active development; not yet released to NuGet.

## Roadmap

- Additional `ITelemetryBackend` adapters: Tempo (TraceQL), Jaeger (HTTP), Loki (LogQL), ClickHouse-SQL (SigNoz / Uptrace / HyperDX / qryn / Highlight / BetterStack).
- `dotnet tool` packaging (`PackAsTool`) for global installation.
- E2E test tier driven by `testapp` against a running backend.
- CI matrix across Windows, Linux, macOS.

## Contributing

Bug reports, ideas, and pull requests are welcome via the [GitHub issue tracker](https://github.com/mmc41/opentelquery/issues). When contributing code:

- Follow the TDD flow used in the repo: write the failing unit or E2E test first, then implement (see `CLAUDE.md` §Boundaries).
- For a new backend, follow the seam in `CLAUDE.md` §"Adding a new backend" — new project under `src/telemetry-backends/`, one case in `BackendFactory`, mirror the OpenObserve test layout.
- Run `dotnet build opentelquery.sln` and `dotnet test opentelquery.sln` before opening a PR. `TreatWarningsAsErrors=true` will block on any warning — fix the root cause rather than suppressing.

## Community and Support

- [GitHub Issues](https://github.com/mmc41/opentelquery/issues) — bug reports, feature requests, and questions.
- Maintainer: [@mmc41](https://github.com/mmc41).

## License

Apache License 2.0 — see [LICENSE](LICENSE) for the full text.
