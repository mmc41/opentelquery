using System.CommandLine;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class SchemaCommand
{
    private static readonly string[] ValidStreamTypes = { "logs", "traces", "metrics", "enrichment_tables" };

    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var streamArg = new Argument<string>("stream")
        {
            Description = "Stream name.",
        };
        var typeOpt = new Option<string>("--type")
        {
            Description = "Stream type (logs, traces, metrics, enrichment_tables).",
            Required = true,
        };

        var command = new Command("schema", "Fetch the schema of a single stream. Emits a SchemaBundle.");
        command.Add(streamArg);
        command.Add(typeOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var streamName = parseResult.GetRequiredValue(streamArg);
            var streamType = parseResult.GetRequiredValue(typeOpt);

            if (!ValidStreamTypes.Contains(streamType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"--type must be one of: {string.Join(", ", ValidStreamTypes)}. Got '{streamType}'.");

            streamType = streamType.ToLowerInvariant();

            var backend = backendFactory();
            try
            {
                var body = await backend.GetStreamSchemaAsync(streamName, streamType, ct);
                var fields = StreamsAssembler.ParseSchemaOnly(body);
                var streamSettings = StreamsAssembler.ParseSettingsOnly(body);

                var now = time.GetUtcNow();
                var endUs = now.ToUnixTimeMilliseconds() * 1000L;

                var header = BundleBuilder.BuildHeader(
                    schema: SchemaBundle.CurrentSchema,
                    description: SchemaBundle.KeyConvention,
                    command: "schema",
                    backend: backend,
                    startTimeUs: endUs,
                    endTimeUs: endUs,
                    lookbackMinutes: 0,
                    queryInfo: new QueryInfo(
                        TraceId: null,
                        RequestedSize: 0,
                        From: 0,
                        Returned: fields.Count));

                await stdout.WriteLineAsync(JsonOutput.FormatObject(new SchemaBundle(header, streamName, streamType, fields, streamSettings)));
                return 0;
            }
            finally
            {
                (backend as IDisposable)?.Dispose();
            }
        });

        return command;
    }
}
