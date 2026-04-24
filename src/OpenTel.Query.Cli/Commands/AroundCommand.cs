using System.CommandLine;
using System.Globalization;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class AroundCommand
{
    private static readonly string[] ValidStreamTypes = { "logs", "traces" };

    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var atOpt = new Option<string>("--at")
        {
            Description = "Target timestamp: ISO-8601 (2026-04-20T14:00:00Z) or Unix microseconds (integer).",
            Required = true,
        };
        var streamOpt = new Option<string?>("--stream")
        {
            Description = "Stream name. Defaults to the stream configured for the active backend.",
        };
        var streamTypeOpt = new Option<string>("--stream-type")
        {
            Description = "Stream type (logs or traces). Default: logs.",
            DefaultValueFactory = _ => "logs",
        };
        var sizeOpt = new Option<int>("--size")
        {
            Description = "Total number of records (±size/2 around the target). Default: 20.",
            DefaultValueFactory = _ => 20,
        };

        var command = new Command("around", "Fetch log records surrounding a timestamp. Emits a LogBundle.");
        command.Add(atOpt);
        command.Add(streamOpt);
        command.Add(streamTypeOpt);
        command.Add(sizeOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var atRaw = parseResult.GetRequiredValue(atOpt);
            var streamOverride = parseResult.GetValue(streamOpt);
            var streamType = parseResult.GetValue(streamTypeOpt) ?? "logs";
            var size = parseResult.GetValue(sizeOpt);

            if (!ValidStreamTypes.Contains(streamType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"--stream-type must be one of: {string.Join(", ", ValidStreamTypes)}. Got '{streamType}'.");

            var atUs = ParseTimestamp(atRaw);

            var backend = backendFactory();
            try
            {
                var streamName = streamOverride ?? backend.DefaultStreamName;
                var body = await backend.GetAroundAsync(streamName, streamType.ToLowerInvariant(), atUs, size, ct);
                var hits = QueryCommand.ExtractHits(body);
                var logs = LogAssembler.Assemble(hits);

                var halfWindowUs = 5L * 60 * 1_000_000L;
                var startUs = atUs - halfWindowUs;
                var endUs = atUs + halfWindowUs;

                var header = BundleBuilder.BuildHeader(
                    schema: LogBundle.CurrentSchema,
                    description: LogBundle.KeyConvention,
                    command: "around",
                    backend: backend,
                    startTimeUs: startUs,
                    endTimeUs: endUs,
                    lookbackMinutes: 10,
                    queryInfo: new QueryInfo(
                        TraceId: null,
                        RequestedSize: size,
                        From: 0,
                        Returned: logs.Count));

                await stdout.WriteLineAsync(JsonOutput.FormatObject(new LogBundle(header, logs)));
                return 0;
            }
            finally
            {
                (backend as IDisposable)?.Dispose();
            }
        });

        return command;
    }

    public static long ParseTimestamp(string input)
    {
        var trimmed = input.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asLong))
            return asLong;

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var absolute))
            return absolute.ToUnixTimeMilliseconds() * 1000L;

        throw new InvalidOperationException(
            $"--at value '{input}' is not a valid timestamp. Use ISO-8601 (2026-04-20T14:00:00Z) or Unix microseconds.");
    }
}
