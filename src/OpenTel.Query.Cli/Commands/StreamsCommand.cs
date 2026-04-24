using System.CommandLine;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class StreamsCommand
{
    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var typeOpt = new Option<string?>("--type")
        {
            Description = "Restrict to one stream type: logs, traces, metrics, enrichment_tables.",
        };
        var fetchSchemaOpt = new Option<bool>("--fetch-schema")
        {
            Description = "Include per-field schema in each stream entry.",
            DefaultValueFactory = _ => false,
        };

        var command = new Command("streams", "List streams in the configured backend. Emits a StreamsBundle.");
        command.Add(typeOpt);
        command.Add(fetchSchemaOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var streamType = parseResult.GetValue(typeOpt);
            var fetchSchema = parseResult.GetValue(fetchSchemaOpt);

            var backend = backendFactory();
            try
            {
                var body = await backend.ListStreamsAsync(streamType, fetchSchema, ct);
                var streams = StreamsAssembler.ParseList(body);

                var now = time.GetUtcNow();
                var endUs = now.ToUnixTimeMilliseconds() * 1000L;

                var header = BundleBuilder.BuildHeader(
                    schema: StreamsBundle.CurrentSchema,
                    description: StreamsBundle.KeyConvention,
                    command: "streams",
                    backend: backend,
                    startTimeUs: endUs,
                    endTimeUs: endUs,
                    lookbackMinutes: 0,
                    queryInfo: new QueryInfo(
                        TraceId: null,
                        RequestedSize: 0,
                        From: 0,
                        Returned: streams.Count));

                await stdout.WriteLineAsync(JsonOutput.FormatObject(new StreamsBundle(header, streams)));
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
