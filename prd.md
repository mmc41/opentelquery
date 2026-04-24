# OpenTel.Query

> A vendor-neutral CLI for querying OpenTelemetry-compatible observability backends (traces, logs, streams, schema) that emits self-describing JSON bundles optimized for both human operators and LLM agents.

## Vision and Purpose

Modern distributed systems emit telemetry into a growing zoo of observability backends (OpenObserve, Jaeger, Tempo, Loki, Datadog, Honeycomb, Elastic APM, …), each with its own query dialect and response shape. OpenTel.Query exists so a single tool — and a single set of muscle memory — works across all of them. The same `query --service Api --status ERROR --since "2h ago"` invocation should produce the same kind of answer whether the data sits in OpenObserve SQL, Tempo TraceQL, or Jaeger's HTTP API.

## Target Audience

| Persona | Role/Context | Primary Need |
|---------|-------------|--------------|
| Backend engineer debugging production | Troubleshoots live incidents from a terminal, often knows a symptom but not a trace id | Narrow a large telemetry window to a handful of relevant traces or logs using filters (service, operation, status, duration, HTTP code) |
| LLM agent diagnosing a system | Autonomous coding/ops agent with shell access; must form hypotheses, call the right query, interpret the result | Structured, self-describing output it can reason over without cross-referencing vendor documentation; one-shot `--help` that describes the full surface |
| Site Reliability Engineer on-call | Triages alerts, correlates logs with traces across services, needs context around a known timestamp | A correlated drill-down path (incident-time → span tree → surrounding logs) that works identically regardless of the backend in play |

## Problem Statement

A backend engineer or LLM who needs to debug an incident today faces three compounding frictions: (1) **disparate query languages** — OpenObserve expects SQL, Tempo expects TraceQL, Loki expects LogQL, Jaeger expects REST-with-tags; (2) **vendor-shaped responses** that require per-backend parsing and hide correlation metadata the caller just asked for; (3) **no correlated drill-down path** — each UI handles traces *or* logs, not both, and context-around-a-timestamp is often missing entirely. The result is lost minutes per incident and LLM agents that can't reliably debug beyond the single backend they were trained with.

## Key Features

| Feature | Benefit | Status |
|---------|---------|--------|
| Unified filter surface across backends (`--service`, `--op-like/-regex/-glob`, `--status`, `--attr`, `--http-status`, `--duration-gt`) | One mental model instead of N query-language dialects | GA for OpenObserve adapter; Planned for Tempo/Jaeger/Loki/Datadog |
| Self-describing JSON bundles (each response carries `$schema`, `command`, `source`, `window`, `query_info`) | LLMs can interpret results without vendor docs; human reviewers can see the filter echo-back | GA |
| Correlated drill-down commands (`query` → `lookup` → `logs` → `around`) | Same CLI walks from a symptom to a span tree to surrounding logs in four invocations | GA |
| Stream and schema discovery (`streams`, `schema <name> --type`) | Caller can learn what fields exist before writing a filter — critical for LLMs that don't already know the backend's schema | GA |
| Relative + absolute time vocabulary (`--since "2h ago"` / `--until 2026-04-20T14:00:00Z`) | Same syntax for recent and historical incidents; no backend-specific epoch math | GA |
| Enhanced root `--help` with worked examples and recursive per-command dump | One shell call gives the LLM the entire CLI surface | GA |

## Architecture Overview

Thin client CLI with a pluggable adapter layer. Each backend adapter implements `ITelemetryBackend` and maps the common filter/query vocabulary to the backend's native query language. The active adapter is selected at startup via the `Backend` configuration key (default: `openobserve`); a single `switch` in `BackendFactory` constructs the concrete implementation.

```text
┌───────────────────────────────────────────────────────────────────┐
│                       OpenTel.Query CLI                           │
│  query │ lookup │ logs │ around │ streams │ schema                │
└──────────────────────────┬────────────────────────────────────────┘
                           │   Common: FilterSpec + LogsFilterSpec
                           │   Common: TimeRange
                           │   Common: Self-describing bundles
                           ▼
┌───────────────────────────────────────────────────────────────────┐
│                    ITelemetryBackend (adapter)                    │
└──┬──────────────┬────────────────┬────────────────┬───────────────┘
   │              │                │                │
   ▼              ▼                ▼                ▼
┌──────────┐ ┌──────────┐ ┌──────────────┐ ┌──────────────┐
│OpenObserve│ │ Tempo /  │ │   Jaeger     │ │  Loki /      │
│  (REST)   │ │ TraceQL  │ │ (HTTP+JSON)  │ │  Datadog …   │
└──────────┘ └──────────┘ └──────────────┘ └──────────────┘
```

**Projects:** `OpenTel.Query.Core` (abstractions, domain models, assemblers, bundle shapes), `OpenTel.Query.Cli` (exe, commands, composition root), `OpenTel.Query.Backends.<Name>` (one per adapter; `OpenObserve` is the reference).
**Technology stack:** .NET 10, `System.CommandLine` 2.0.7, `System.Text.Json`, `Microsoft.Extensions.Configuration`.
**Deployment model:** Single-binary CLI; distributable as a `dotnet tool` (currently shipped as a plain exe — `PackAsTool` wiring is planned) and/or copied into any repo.
**Key integrations:** Any OTLP-fed observability backend exposing an HTTP query API.

## Key Differentiators

OpenTel.Query is the only general-purpose observability query CLI that emits self-describing, schema-versioned bundles explicitly shaped for LLM consumption, letting one tool drive incident triage across multiple OpenTelemetry backends from a shell. (confidence: `hypothesis` — no competitor scan performed)

| Differentiator | How It Compares | Evidence |
|---------------|----------------|----------|
| Vendor-neutral query vocabulary | Backend-native tools (`tempo-cli`, OpenObserve UI, Jaeger UI, Grafana Explore) each expose their own dialect | Single `FilterSpec` compiles to each adapter's native query; demonstrated in OpenObserve adapter (confidence: `measured` for OpenObserve; `hypothesis` for others) |
| LLM-first output shape | Raw backend responses omit filter echo-back and lack schema identifiers | Every bundle includes `$schema`, `$description`, echoed `filters`, `window`, `source` (confidence: `measured`) |
| One CLI for six correlated commands | Traces-only (Jaeger) or logs-only (Loki) CLIs require users to stitch context themselves | Single binary, six subcommands, shared time-range + filter vocabulary (confidence: `measured`) |
| Runs entirely locally against existing credentials | Hosted observability copilots require vendor-specific accounts and egress | Config via `appsettings.json` + OS user-secrets or env vars; no cloud dependency (confidence: `measured`) |

## What This Product Is Not

- **Not an observability backend itself.** It does not ingest, store, index, or alert on telemetry. It is a query-side tool.
- **Not a replacement for a backend's GUI.** For long-form exploration, dashboards, and alerting, use the vendor UI.
- **Not a metrics client.** Phase 1 covers traces and logs (with streams/schema discovery); metrics and PromQL are out of scope for v1.
- **Not a universal OTLP client.** It speaks to backends' *query* APIs, not to OTLP ingestion endpoints.
- **Not a cross-backend aggregator.** A single invocation targets one configured backend; it does not federate queries across multiple backends.

---

# Part 2: PRD-lite

## Product Context

OpenTel.Query was extracted from an internal tool (`OpenObserveQuery`) originally built for a .NET web application whose telemetry lands in a self-hosted OpenObserve instance. The split into `OpenTel.Query.Core` + `OpenTel.Query.Cli` + `OpenTel.Query.Backends.OpenObserve` introduced the `ITelemetryBackend` abstraction; the resulting codebase has **140 passing unit tests** across the three test projects (Core, Cli, Backends.OpenObserve) and live-verified end-to-end behavior against OpenObserve. The next adapter (prioritized with early adopters — likely Tempo or Jaeger given community prevalence) plugs in as a new `OpenTel.Query.Backends.<Name>` project and one case in `BackendFactory`.

## User Classes and Characteristics

| User Class | Characteristics | Frequency of Use | Technical Proficiency |
|-----------|-----------------|-------------------|---------------------|
| Backend engineer (incident) | Knows symptoms, needs correlation; runs the tool a handful of times per incident | Weekly (per-incident spikes) | High |
| LLM coding/ops agent | Invokes the CLI as a shell tool; parses structured JSON | Continuous within an agent session | High (machine) |
| SRE on-call | Needs fast drill-down from alert timestamp to span tree to logs | Daily during on-call rotations | High |
| Platform engineer (setup) | Configures adapters, manages user-secrets, distributes the tool | Monthly | High |

## Operating Environment

- **Runtime**: .NET 10 SDK (`net10.0` target framework)
- **OS**: Windows, Linux, macOS (anywhere .NET 10 runs)
- **Distribution**: Copy-out friendly (single directory + tests), packaged as `dotnet tool` (`PackAsTool=true`)
- **Network**: Outbound HTTP(S) to configured backend URL
- **Credentials**: User-secrets (id via `appsettings.json`) or environment variables

## Constraints and Dependencies

### Design and Implementation Constraints

- Must be a single-binary CLI invocable from any shell; no daemon, no GUI.
- All user-supplied filter values must be escaped before embedding in any backend query (SQL injection class).
- Output must be valid JSON on stdout; errors go to stderr with non-zero exit code.
- No secrets ever written to stdout, logs, or bundles.

### Assumptions

- The target backend exposes an HTTP(S) query API reachable from the caller's machine.
- Authentication follows one of the common patterns (Basic, Bearer, header-based).
- The backend's trace data is OpenTelemetry-compatible (span semantics follow OTel conventions).

### Dependencies

- **Backend query API** — version and availability drive adapter compatibility.
- **.NET 10 SDK** — consumer must have it installed (or use a self-contained publish).
- **System.CommandLine 2.0.7** — CLI parsing.
- **Microsoft.Extensions.Configuration.\*** — layered config (JSON, user-secrets, environment variables).

## System Features

### Filtered Trace Query (`query`)

**Description**: Lists recent traces matching a filter spec, with full span trees attached, as a `TraceBundle`.

**Functional Requirements**:

- FR-Q.1: The command MUST accept `--service`, `--op-like`/`--op-regex`/`--op-glob` (mutually exclusive), `--status`, `--attr <k=v>` (repeatable), `--http-status`, `--duration-gt`, `--since`, `--until`, `--size`, `--from`.
- FR-Q.2: The command MUST echo the active filter spec back into `query_info.filters` on every response.
- FR-Q.3: When no filter is supplied, the command MUST fall back to the backend's native "latest traces" path (where available) to preserve aggregate enrichments like root-service and per-service span counts.
- FR-Q.4: The command MUST emit a `TraceBundle` with `$schema = "opentel-query-trace/v1"`.

### Single-Trace Lookup (`lookup <trace-id>`)

**Description**: Fetches all spans for a specific trace id across the configured time window and assembles them into a span tree.

**Functional Requirements**:

- FR-L.1: The command MUST accept a positional `trace-id` argument and `--since`/`--until`.
- FR-L.2: The command MUST emit a `TraceBundle` with exactly one trace when the id is found.
- FR-L.3: When the id has no matches, the command MUST emit an empty `TraceBundle` and exit 0.

### Log Search (`logs`)

**Description**: Searches logs by trace id, service, severity, and optional full-text or field-scoped pattern matching.

**Functional Requirements**:

- FR-LG.1: The command MUST accept `--trace-id`, `--service`, `--level`, `--match <text>` (full-text), and `--match-field <name>` with one of `--match-like`/`--match-regex`/`--match-glob` (mutually exclusive). CLI args are parsed into a `LogsFilterSpec` domain object that the adapter translates to its native query.
- FR-LG.2: The command MUST map `--level` to the backend-native severity column (e.g., OTel `severity` for OpenObserve).
- FR-LG.3: The command MUST emit a `LogBundle` with `$schema = "opentel-query-log/v1"`.

### Context Around a Timestamp (`around`)

**Description**: Fetches log records immediately before and after a target timestamp within a backend-bounded window.

**Functional Requirements**:

- FR-A.1: The command MUST accept `--at <ISO-8601-or-microseconds>`, `--stream`, `--stream-type`, `--size`. `--stream` is optional; when omitted, the adapter's `DefaultStreamName` is used.
- FR-A.2: The command MUST emit a `LogBundle` with `command = "around"`.

### Stream Discovery (`streams`)

**Description**: Lists streams (logs, traces, metrics, enrichment tables) available in the configured organization.

**Functional Requirements**:

- FR-S.1: The command MUST accept an optional `--type` filter and `--fetch-schema` to include per-field schema inline.
- FR-S.2: The command MUST emit a `StreamsBundle` with `$schema = "opentel-query-streams/v1"`.

### Schema Introspection (`schema <stream> --type`)

**Description**: Returns the field list and settings for a single stream so a caller can construct accurate filters.

**Functional Requirements**:

- FR-SC.1: The command MUST validate `--type` against the allowed set (`logs`, `traces`, `metrics`, `enrichment_tables`).
- FR-SC.2: The command MUST emit a `SchemaBundle` with `$schema = "opentel-query-schema/v1"` including `fields[]` with name and type.

### Pluggable Backend Adapter (`ITelemetryBackend`)

**Description**: Abstraction that lets new backends plug into the CLI without changing command code.

**Functional Requirements**:

- FR-AD.1: The interface MUST expose methods for: latest traces (no-filter path for FR-Q.3), filtered trace-id search, span fetch by id set, log search, around, stream list, stream schema.
- FR-AD.2: Each method MUST accept already-validated domain objects (`FilterSpec`, `LogsFilterSpec`, time bounds, paging) and return raw JSON for the command layer to assemble into bundles. Commands MUST NOT construct backend-native query syntax themselves.
- FR-AD.3: Adapters MUST escape all user-supplied values before embedding in native query syntax.
- FR-AD.4: Adapters MUST throw a typed exception (`TelemetryBackendException`) carrying backend name, HTTP status, reason, response body, and optional hint.
- FR-AD.5: Adapters MUST expose metadata consumed by `BundleBuilder` to populate `source`: `BackendName` (identifier used in bundle `source.backend`), `Host` (for `source.host`), `Properties` (free-form `IReadOnlyDictionary<string, string?>` for backend-specific metadata — e.g. OpenObserve puts `organization` and `stream` here), and `DefaultStreamName` (fallback for FR-A.1).

## External Interface Requirements

### User Interfaces

Command-line only. Root `--help` shows a unified cheat-sheet: description, commands, worked examples, and each subcommand's full help recursively. Per-subcommand `--help` shows the default System.CommandLine layout.

### Software Interfaces

| System | Interface Type | Purpose | Data Format |
|--------|---------------|---------|-------------|
| Observability backend (HTTP API) | REST/HTTP(S) | Queries, stream listing, schema | JSON |
| `appsettings.json` | File | Non-secret config: `Backend` (selects adapter, default `openobserve`), `Query:LookbackMinutes`, `UserSecretsId` | JSON |
| User-secrets store | Filesystem (OS-specific path) | Credential storage: `Telemetry:Headers` (OTLP header string incl. `Authorization`, `organization`, `stream-name`) | JSON |
| Environment | Environment variables | Overrides for everything above (e.g. `Backend`, `Telemetry__Headers`, `OpenObserve__Host`) | UTF-8 strings |
| Stdout | Text stream | Self-describing JSON bundles | Pretty-printed UTF-8 JSON |
| Stderr | Text stream | Error messages, hints | UTF-8 text |

## Quality Attributes

| Attribute | Target | Measurement |
|-----------|--------|-------------|
| Performance (query latency) | p95 ≤ 2 s for a 1-hour window with size=50 on a local backend | Stopwatch instrumentation in a smoke harness |
| Output parseability | 100% of bundles parse as valid JSON and match their declared `$schema` | JSON Schema validation in test suite |
| Input safety | Zero SQL/injection escapes across pathological apostrophe/semicolon/backslash inputs | Unit tests asserting apostrophe escape (`'` → `''`) and identifier validator rejection of non-`[A-Za-z0-9_.]` keys |
| Portability | Runs unmodified on Windows, Linux, macOS with .NET 10 SDK | CI matrix (planned) |
| Discoverability | Full command+option surface obtainable from one `--help` invocation | Manual inspection; regression test on help snapshot |
| Secret hygiene | Secrets never appear in stdout, exceptions, or bundles | Grep assertion in test fixture |

## Data Requirements

### Data Model Overview

The tool is stateless. The only structured data it produces is its four bundle types:

- `TraceBundle` — header + `traces[]` (nested span trees)
- `LogBundle` — header + `logs[]` (flat, time-ordered records)
- `StreamsBundle` — header + `streams[]` (metadata + optional schema)
- `SchemaBundle` — header + `fields[]` (name + type)

All four share `BundleHeader` = `$schema`, `$description`, `command`, `source`, `window`, `query_info`, where:

- `source` = `{ tool: "OpenTel.Query", backend: <BackendName>, host: <Host>, properties: { … } }`. The `properties` map is backend-supplied free-form metadata; the OpenObserve adapter populates `{ organization, stream }`.
- `window` = `{ start_time, end_time, start_time_us, end_time_us, lookback_minutes }`.
- `query_info` = `{ trace_id, requested_size, from, returned, filters }` where `filters` echoes the parsed `FilterSpec`/`LogsFilterSpec` (or is `null` when no filter was supplied).

### Data Integrity and Retention

The CLI does not persist data. Any retention is the responsibility of the caller (typically stdout capture into a file).

## Glossary

| Term | Definition |
|------|-----------|
| Adapter | A backend-specific implementation of `ITelemetryBackend` (e.g. `OpenObserveBackend`) |
| Bundle | A self-describing JSON envelope emitted by one CLI command |
| `FilterSpec` | Internal common data structure holding parsed trace-query filter values (service, operation pattern, status, attributes, HTTP status, duration) |
| `LogsFilterSpec` | Sibling data structure for `logs` — trace id, service, level, free-text match, field-scoped match |
| OTLP | OpenTelemetry Protocol; the wire format for trace/log/metric ingestion |
| Span tree | Nested structure of spans for a single trace, with parent-child relationships resolved |
| Stream | A logical partition within an observability backend (e.g. `default`) |

---

# Part 3: Test Information

## Test Automation Approach

**Strategy**: Heavy unit-test base validating the shared command/filter/assembler layer; one live smoke test per adapter against a running backend instance; an E2E tier built on the `testapp` OpenTelemetry signal generator (see `testapp/README.md`) — `POST /telemetry/flush` on testapp provides deterministic ingest barriers so E2E tests don't have to wait out OTLP batch intervals.
**Frameworks**: `xunit.v3` 3.2.1, `Microsoft.NET.Test.Sdk` 17.13.0. CLI wired through `System.CommandLine`'s parse model so commands can be invoked under test.
**CI/CD integration**: Tests run on every PR; live smokes and E2E runs are gated behind a configured adapter + running backend and are opt-in.

| Test Level | Scope | Automation | Execution Frequency |
|-----------|-------|-----------|-------------------|
| Unit | Filter SQL generation, time-range parsing, duration parsing, HTTP status parsing, bundle serialization, JSON shape | Automated (xunit) | Every commit |
| Integration (in-process) | CLI wiring: option parsing, mutually-exclusive flags, command-to-backend propagation via a fake `ITelemetryBackend` | Automated (xunit) | Every commit |
| Live smoke | One round-trip per command per adapter against a running backend | Automated when backend configured; manual otherwise | Per release / per adapter change |
| E2E (planned) | testapp drives OTLP traffic to a running backend → flush → CLI query → assert bundle contents | Planned (opt-in; needs running backend + testapp) | Per release |

## Test Oracles

| Oracle Type | Application | Example |
|------------|-------------|---------|
| Expected-output comparison | SQL fragment assertions | Assert `service_name = 'Api'` appears in the generated predicate |
| Structural (JSON schema) | Bundle shape | Assert `$schema` value matches declared constant; assert required header fields present |
| Invariant | Filter round-trip | Any filter in → same filter echoed in `query_info.filters` out |
| Round-trip | Time-range parsing | Parse `"2h ago"` → microseconds → diff with provided `TimeProvider` = 7 200 000 000 |
| Negative | Security | Inputs with `'`, `;`, `--` do not produce syntactically invalid backend queries; unit-tested |
| Side-channel | Adapter URL/body construction | `RecordingHandler` captures the outbound request and asserts method/URL/body shape |

## Test Data

**Approach**: Mostly inline fixture JSON strings in test files (representative backend responses); no external test database.
**Availability**: All fixtures committed alongside the tests; no external service required for the unit tier.
**Sensitive data handling**: No PII in fixtures; any production-shaped fixture is anonymized before commit. Live smoke runs use the operator's own backend credentials, never committed.

| Data Category | Source | Refresh Frequency |
|--------------|--------|-------------------|
| Trace span fixtures | Inline JSON literals in `CliBuilderTests`, `TraceAssemblerTests` | Per test refactor |
| Log record fixtures | Inline JSON literals in `LogAssemblerTests` | Per test refactor |
| Stream/schema fixtures | Inline JSON literals in `StreamsAssemblerTests` | Per test refactor |
| Backend HTTP fixtures | `RecordingHandler` intercepting real request shape | Per adapter addition |
| Live-smoke traces/logs | Operator's running backend | Per session (not version-controlled) |

---

## Standards and Specifications

- OpenTelemetry — [https://opentelemetry.io/docs/specs/otel/](https://opentelemetry.io/docs/specs/otel/) (trace and log semantic conventions assumed for vendor-neutral filter names)
- OpenObserve — [https://openobserve.ai/docs/](https://openobserve.ai/docs/) (reference adapter's API surface)
- Grafana Tempo — [https://grafana.com/docs/tempo/latest/api_docs/](https://grafana.com/docs/tempo/latest/api_docs/) (planned adapter target)
- Jaeger HTTP API — [https://www.jaegertracing.io/docs/latest/apis/](https://www.jaegertracing.io/docs/latest/apis/) (planned adapter target)
- Grafana Loki — [https://grafana.com/docs/loki/latest/reference/api/](https://grafana.com/docs/loki/latest/reference/api/) (planned adapter target)
- ISO/IEC/IEEE 29148:2018 — requirements engineering vocabulary (informs PRD-lite structure)
- ISO/IEC 25010 — non-functional quality model (informs quality attributes table)
