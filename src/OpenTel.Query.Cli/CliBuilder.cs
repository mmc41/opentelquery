using System.CommandLine;
using OpenTel.Query.Cli.Commands;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;

namespace OpenTel.Query.Cli;

public static class CliBuilder
{
    public const string RootDescription = "Query traces and logs from an OpenTelemetry-compatible backend (OpenObserve, ...). "
        + "Credentials are loaded from user secrets (id configured via `UserSecretsId` in appsettings.json) or from environment variables. "
        + "The active backend is chosen via the `Backend` configuration key (default: openobserve). "
        + "Commands: query (traces with filters), lookup (single trace), logs (log search), around (log context), streams (list streams), schema (single-stream fields).";

    public static RootCommand Build(
        Func<ITelemetryBackend> backendFactory,
        QuerySettings settings,
        TimeProvider time,
        TextWriter stdout)
    {
        var root = new RootCommand(RootDescription);
        root.Add(QueryCommand.Create(backendFactory, settings, time, stdout));
        root.Add(LookupCommand.Create(backendFactory, settings, time, stdout));
        root.Add(LogsCommand.Create(backendFactory, settings, time, stdout));
        root.Add(AroundCommand.Create(backendFactory, settings, time, stdout));
        root.Add(StreamsCommand.Create(backendFactory, settings, time, stdout));
        root.Add(SchemaCommand.Create(backendFactory, settings, time, stdout));
        HelpCustomization.Configure(root);
        return root;
    }
}
