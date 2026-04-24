using System.CommandLine;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class LookupCommand
{
    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var traceIdArg = new Argument<string>("trace-id")
        {
            Description = "The OpenTelemetry trace id (hex string) to look up.",
        };
        var (sinceOpt, untilOpt) = TimeRangeOptions.Create();

        var command = new Command("lookup", "Fetch all spans for a single trace id. Emits a self-describing TraceBundle with exactly one trace.");
        command.Add(traceIdArg);
        command.Add(sinceOpt);
        command.Add(untilOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var traceId = parseResult.GetRequiredValue(traceIdArg);
            var since = parseResult.GetValue(sinceOpt);
            var until = parseResult.GetValue(untilOpt);

            var (startUs, endUs) = TimeRangeParser.Resolve(since, until, time, settings.LookbackMinutes);
            var lookbackMinutes = (int)((endUs - startUs) / (60L * 1_000_000L));

            var backend = backendFactory();
            try
            {
                var spansJson = await backend.SearchTraceSpansAsync(new[] { traceId }, startUs, endUs, ct);
                var hits = QueryCommand.ExtractHits(spansJson);
                var traces = TraceAssembler.Assemble(hits);

                var bundle = BundleBuilder.BuildTraceBundle(
                    command: "lookup",
                    backend: backend,
                    startTimeUs: startUs,
                    endTimeUs: endUs,
                    lookbackMinutes: lookbackMinutes,
                    queryInfo: new QueryInfo(
                        TraceId: traceId,
                        RequestedSize: 1,
                        From: 0,
                        Returned: traces.Count),
                    traces: traces);

                await stdout.WriteLineAsync(JsonOutput.FormatObject(bundle));
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
