# OpenTel.Query

Vendor-neutral .NET 10 CLI (`OpenTel.Query.Cli`) for querying OpenTelemetry-compatible observability backends (traces, logs, streams, schema). Emits self-describing JSON bundles for both shell operators and LLM agents. Current backend coverage: OpenObserve. Adding Jaeger, Tempo, Loki, Datadog, ClickHouse-SQL, etc. requires only a new adapter project — see `prd.md`.

## Commands

Everything runs from the repo root via `opentelquery.sln`.

```bash
# Build + test the whole solution
dotnet build opentelquery.sln
dotnet test  opentelquery.sln

# Run a single test class or method (xUnit v3 filter syntax)
dotnet test test/OpenTel.Query.Cli.UnitTests --filter "FullyQualifiedName~CliBuilderTests"
dotnet test test/OpenTel.Query.Cli.UnitTests --filter "FullyQualifiedName~CliBuilderTests.Query_NoOptions_DefaultSizeIs50AndBundleShapeIsEmitted"

# Run the CLI (requires Telemetry:Headers in user secrets — UserSecretsId is in src/OpenTel.Query.Cli/appsettings.json)
dotnet run --project src/OpenTel.Query.Cli -- --help
dotnet run --project src/OpenTel.Query.Cli -- query --service Api --since "15m ago"
dotnet run --project src/OpenTel.Query.Cli -- streams --type logs

# Run testapp (OTel signal generator used for E2E)
dotnet run --project testapp --launch-profile http
# port from Urls in testapp/appsettings.json (default http://localhost:5091)
# force-flush OTLP exporters after driving traffic (E2E determinism — beats sleeping for the OTLP batch interval):
curl -X POST http://localhost:5091/telemetry/flush
```

`scripts/setup_dev_local_openobserve.ps1` and `scripts/run_dev_local_openobserve.ps1` spin up a local OpenObserve against `./otel_storage/` for manual testing.

## Testing

- **Framework**: xUnit v3 (`xunit.v3 3.2.1`). Test projects: `test/OpenTel.Query.Cli.UnitTests`, `test/OpenTel.Query.Core.UnitTests`, `test/OpenTel.Query.Backends.OpenObserve.UnitTests`, `test/TestApp.UnitTests`.
- **Single test**: see `--filter "FullyQualifiedName~..."` examples in Commands.
- **TimeProvider**: tests that need a `TimeProvider` should inject a local `FakeTimeProvider` subclass (see existing test files), not `TimeProvider.System` — keeps tests deterministic.
- **HTTP shape tests**: the OpenObserve adapter tests use a `RecordingHandler` to assert the outgoing request shape; mirror that pattern for new backends.

## Verification

After every change, run from the repo root:

1. `dotnet build opentelquery.sln` — `TreatWarningsAsErrors=true` will fail on any warning; fix the root cause, don't suppress.
2. `dotnet test opentelquery.sln` — all four test projects must pass.

## Architecture: the backend-adapter seam

The solution layers a CLI exe over a vendor-neutral Core library, with each backend as its own adapter project under `src/telemetry-backends/`. Understanding this seam is the single most important thing for any non-trivial change.

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

- **Adapters own the query language.** `ITelemetryBackend` methods take `FilterSpec`/`LogsFilterSpec`, not SQL strings. Commands never construct backend queries. `OpenObserveSqlTranslator.ToTracePredicate`/`ToLogsPredicate` live in the adapter, not Core — so a future non-SQL backend (Jaeger HTTP, Tempo TraceQL) plugs in without touching command code.
- **Backend selection is one `switch` in `Cli/BackendFactory.cs`.** It reads the `Backend` config key (default `"openobserve"`) and returns an `ITelemetryBackend`.
- **Bundles are self-describing.** Every response includes `$schema` (e.g. `opentel-query-trace/v1`), `source.backend`, `source.properties` (backend-specific metadata like `organization`/`stream`), `window`, and `query_info.filters`. Schemas are backend-independent — only `source.backend`/`source.properties` differ. `BundleBuilder.BuildHeader` is the single point that constructs these from an `ITelemetryBackend`.
- **No DI container.** Composition is functional: a `Func<ITelemetryBackend>` is passed into `CliBuilder.Build` and each command calls the factory inside its `SetAction` so a fresh backend is created per invocation and disposed in `finally`. Tests swap the factory for a fake.

## Adding a new backend

1. Create `src/telemetry-backends/<name>/OpenTel.Query.Backends.<Name>.csproj` (library, references Core).
2. Implement `ITelemetryBackend`. Expose a static `Create(IConfiguration)` factory.
3. Surface backend-specific query translation as static helpers (pattern: `OpenObserveSqlTranslator`). Commands must not need to know the translator exists.
4. Add one `case` to `src/OpenTel.Query.Cli/BackendFactory.cs` and a `<ProjectReference>` from `OpenTel.Query.Cli.csproj`.
5. Add `test/OpenTel.Query.Backends.<Name>.UnitTests/` mirroring the OpenObserve adapter tests (HTTP client with `RecordingHandler`, translator tests, settings tests).
6. Register all four csprojs in `opentelquery.sln`.

`FilterSpec`/`LogsFilterSpec`, `TraceAssembler`, `LogAssembler`, `StreamsAssembler`, and the bundle types are backend-independent — reuse as-is. Only translation and HTTP shape belong in the adapter.

## testapp

`testapp/` is an ASP.NET Core 10 minimal-API that emits OpenTelemetry signals on demand for E2E testing. Two non-obvious test-data-coverage facts the catalog may bury:

- `/traces/ok` is the only endpoint producing `span_status = OK`.
- `/traces/linked` is the only endpoint that populates the `links` column (covers `SpanLink` parsing).

Read `testapp/README.md` (linked under Detail References) before adding endpoints, configuring vendors, or writing new E2E flows.

## Conventions enforced by the build

- **`TargetFramework: net10.0`** on every csproj.
- **`TreatWarningsAsErrors: true`** — warnings fail the build. Fix the root cause; don't suppress.
- **Nullable + ImplicitUsings enabled** on every csproj.
- **No `ManagePackageVersionsCentrally`** — each csproj pins its own package versions.

## Configuration chain

CLI and testapp use the same layered pattern:

1. `appsettings.json` (checked in) — defaults and non-secret config.
2. User secrets (id declared in `appsettings.json`) — `Telemetry:Headers` (OTLP header string containing `Authorization`, `organization`, `stream-name`).
3. Environment variables — override everything (e.g. `Telemetry__Headers`, `Backend`, `Query__LookbackMinutes`).

The CLI additionally reads `Backend` (default `openobserve`) and `Query:LookbackMinutes` (default window when `--since`/`--until` are omitted). The OpenObserve adapter reads `Telemetry:Headers` and `OpenObserve:Host`.

## Boundaries

### Always Do

- Write the failing unit or E2E test first when adding a feature (TDD).
- When the user or a review flags a bug, add a failing unit/E2E test that reproduces it before fixing.
- Use `Microsoft.Extensions.Logging.ILogger`/`ILoggerFactory` for logging in main code; only the composition root may wire a specific logger implementation (e.g. NLog).
- Run Verification (`dotnet build` + `dotnet test`) before marking work complete.

### Ask First

- Adding a new backend — confirm the target backend and scope before scaffolding; the seam in Architecture must hold.
- Changes to bundle `$schema` versions or `BundleHeader` shape — these are wire-format-stable; surface options + recommendation before changing.
- Removing or renaming any of `FilterSpec`/`LogsFilterSpec`/`ITelemetryBackend` members — public to all adapters.

### Never Do

- NEVER mock `ILogger`/`ILoggerFactory` in tests — use a real logger or `NullLogger<T>` instead.
- NEVER construct backend-specific queries (SQL, TraceQL, etc.) in `OpenTel.Query.Cli` or `OpenTel.Query.Core` — translation belongs in the adapter (see Architecture).
- NEVER commit `Telemetry:Headers` or other auth material — they live in user secrets or environment variables, not `appsettings.json`. Note: this rule is advisory; no pre-commit hook enforces it.

## When Asking Questions

- Ask one question at a time; wait for a reply before the next.
- For simple clarifications ("did you mean X or Y?", "which file?"): a concise question is fine.
- For subjective tradeoffs, architectural choices, or design decisions, each question must include:
  - **Purpose** — why this decision matters in this context.
  - **Options** — 2-3 concrete choices with benefits and drawbacks for each.
  - **Recommendation** — the AI's preferred option and the reasoning behind it.
- Search docs and code first; ask only when the answer is external or subjective.
- When the user pushes back on a technical assessment, restate your reasoning before reversing — user decisions and explicit instructions still take precedence.

## Detail References

- `prd.md` — read when scoping new features, new backends, or interpreting product intent; contains the full product requirements and roadmap.
- `testapp/README.md` — read when adding or interpreting E2E test traffic, or pointing testapp at a new OTLP backend; full endpoint catalog and a multi-vendor configuration table (SigNoz, Uptrace, HyperDX, qryn, Highlight, BetterStack).
