# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`OpenTel.Query` is a vendor-neutral CLI for querying OpenTelemetry-compatible observability backends (traces, logs, streams, schema). It emits self-describing JSON bundles designed for both shell operators and LLM agents. Current backend coverage: OpenObserve. The design goal is that adding Jaeger, Tempo, Loki, or Datadog requires only a new adapter project — see `prd.md` for the full product context.

## Commands

Everything runs from the repo root via `opentelquery.sln`.

```bash
# Build + test the whole solution
dotnet build opentelquery.sln
dotnet test  opentelquery.sln

# Run a single test class or method (xUnit v3 filter syntax)
dotnet test test/OpenTel.Query.Cli.UnitTests --filter "FullyQualifiedName~CliBuilderTests"
dotnet test test/OpenTel.Query.Cli.UnitTests --filter "FullyQualifiedName~CliBuilderTests.Query_NoOptions_DefaultSizeIs50AndBundleShapeIsEmitted"

# Run the CLI (requires Telemetry:Headers in user secrets — see src/OpenTel.Query.Cli/appsettings.json for UserSecretsId)
dotnet run --project src/OpenTel.Query.Cli -- --help
dotnet run --project src/OpenTel.Query.Cli -- query --service Api --since "15m ago"
dotnet run --project src/OpenTel.Query.Cli -- streams --type logs

# Run testapp (OTel signal generator used for E2E)
dotnet run --project testapp --launch-profile http
# port is configured via Urls in testapp/appsettings.json (default http://localhost:5091)
# force-flush OTLP exporters after driving traffic (E2E determinism):
curl -X POST http://localhost:5091/telemetry/flush
```

`scripts/setup_dev_local_openobserve.ps1` and `scripts/run_dev_local_openobserve.ps1` spin up a local OpenObserve against `./otel_storage/` for manual testing.

## Architecture: the backend-adapter seam

The solution is three projects under `src/` plus three test projects under `test/`. Understanding the seam between them is the single most important thing for any non-trivial change.

```
OpenTel.Query.Cli (exe)
  │  composes everything at startup
  │  commands translate CLI args → FilterSpec/LogsFilterSpec → backend call → bundle → stdout JSON
  ▼
OpenTel.Query.Core (library)
  │  ITelemetryBackend — the adapter contract (Abstractions/)
  │  FilterSpec / LogsFilterSpec — vendor-neutral filter models (Filtering/)
  │  Trace/Log/Streams Assembler — parse raw backend JSON into bundle models (Processing/)
  │  BundleHeader / TraceBundle / LogBundle / StreamsBundle / SchemaBundle (Model/)
  ▼
OpenTel.Query.Backends.OpenObserve (library)
     implements ITelemetryBackend; owns all SQL generation (OpenObserveSqlTranslator)
     and HTTP shape against OpenObserve's REST API
```

**Adapters own the query language.** `ITelemetryBackend` methods take `FilterSpec`/`LogsFilterSpec` (domain objects), not SQL strings. Commands never construct backend queries. This is why `OpenObserveSqlTranslator.ToTracePredicate` and `ToLogsPredicate` live in the adapter, not in `Core`. A future non-SQL backend (Jaeger HTTP, Tempo TraceQL) plugs in without touching command code.

**Backend selection is one `switch` in `Cli/BackendFactory.cs`.** Program.cs calls `BackendFactory.Create(cfg)` which reads the `Backend` config key (default `"openobserve"`) and returns an `ITelemetryBackend`. Adding a new backend = new project + one case in that switch + reference it from the Cli csproj.

**Bundles are self-describing.** Every response includes `$schema` (e.g. `opentel-query-trace/v1`), `source.backend` (e.g. `"openobserve"`), `source.properties` (backend-specific metadata like `organization` and `stream`), `window`, and `query_info.filters` echoing back what the caller sent. Schemas are backend-independent — only `source.backend` and `source.properties` differ per adapter. `BundleBuilder.BuildHeader` is the single point that constructs these from an `ITelemetryBackend`.

**No DI container.** Composition is functional: `Func<ITelemetryBackend> backendFactory = () => BackendFactory.Create(cfg)` is passed into `CliBuilder.Build`, and each command calls the factory inside its `SetAction` so a fresh backend is created per command invocation and disposed in `finally`. Tests swap the factory for a fake.

## Adding a new backend

1. Create `src/telemetry-backends/<name>/OpenTel.Query.Backends.<Name>.csproj` (library, references Core).
2. Implement `ITelemetryBackend`. Expose a static `Create(IConfiguration)` factory.
3. Surface backend-specific query translation as static helpers (pattern: `OpenObserveSqlTranslator`). Commands must not need to know the translator exists.
4. Add one `case` to `src/OpenTel.Query.Cli/BackendFactory.cs` and a `<ProjectReference>` to `OpenTel.Query.Cli.csproj`.
5. Add `test/OpenTel.Query.Backends.<Name>.UnitTests/` mirroring the OpenObserve adapter tests (HTTP client with `RecordingHandler`, translator tests, settings tests).
6. Register all four csprojs in `opentelquery.sln`.

The `FilterSpec`/`LogsFilterSpec` domain models, `TraceAssembler`, `LogAssembler`, `StreamsAssembler`, and all bundle types are backend-independent and should be reused as-is. Only translation and HTTP shape belong in the adapter.

## testapp

`testapp/` is an ASP.NET Core 10 minimal-API that emits OpenTelemetry signals on demand. It's used to generate realistic telemetry for E2E testing of the CLI. Key endpoints:

- `/traces/simple|ok|linked|nested|parallel|slow|error|distributed|attributes` — each exercises a specific trace/span pattern. `/traces/ok` is the only endpoint producing `span_status = OK`; `/traces/linked` is the only one that populates the `links` column (for `SpanLink` parsing coverage).
- `/logs/levels|structured|exception|scoped|correlated` — log severity, structured params, exceptions, scopes, trace↔log correlation.
- `/scenarios/user-transaction|error-cascade` — composite trace+log+metric scenarios.
- `POST /telemetry/flush` — calls `ForceFlush` on all three providers. E2E tests should hit this after driving traffic instead of sleeping for the OTLP batch interval.

Testapp listening URL is in `testapp/appsettings.json` (`Urls` key). The `/health`, `/shutdown`, and `/telemetry/*` paths are filtered out of ASP.NET Core trace instrumentation so they don't pollute E2E data. See `testapp/README.md` for the full endpoint catalog.

## Conventions enforced by the build

- **`TargetFramework: net10.0`** on every csproj.
- **`TreatWarningsAsErrors: true`** — warnings fail the build. Fix the root cause; don't suppress.
- **Nullable + ImplicitUsings enabled.**
- **xUnit v3** (`xunit.v3 3.2.1`). Tests that need a `TimeProvider` should inject a local `FakeTimeProvider` subclass (see existing test files) rather than using `TimeProvider.System`.
- **No `ManagePackageVersionsCentrally`** — each csproj pins its own package versions.

## User's global guidance (from ~/.claude/CLAUDE.md)

- **TDD for new features**: write the unit or E2E test first.
- **Reproduce before fixing**: when the user or a review flags a bug, add a failing unit/E2E test that reproduces it, then fix.
- **Logging**: use `Microsoft.Extensions.Logging.ILogger`/`ILoggerFactory`. Never mock logger interfaces. Main code should not depend on a specific logger implementation (e.g. NLog) — only the composition root wires one up.

## Configuration chain

Both CLI and testapp use the same layered pattern:

1. `appsettings.json` (checked in) — defaults and non-secret config.
2. User secrets (id declared in `appsettings.json`) — `Telemetry:Headers` (OTLP header string containing `Authorization`, `organization`, `stream-name`).
3. Environment variables — override everything (e.g. `Telemetry__Headers`, `Backend`, `Query__LookbackMinutes`).

The CLI additionally reads `Backend` (default `openobserve`) and `Query:LookbackMinutes` (default window when `--since`/`--until` are omitted). The adapter reads `Telemetry:Headers` and `OpenObserve:Host`.
