using System.CommandLine;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;
using OpenTel.Query.Core.Filtering;
using OpenTel.Query.Core.Model;
using OpenTel.Query.Core.Output;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Cli.Commands;

public static class LogsCommand
{
    public static Command Create(Func<ITelemetryBackend> backendFactory, QuerySettings settings, TimeProvider time, TextWriter stdout)
    {
        var (sinceOpt, untilOpt) = TimeRangeOptions.Create();
        var sizeOpt = new Option<int>("--size")
        {
            Description = "Maximum number of log records to return.",
            DefaultValueFactory = _ => 100,
        };
        var fromOpt = new Option<int>("--from")
        {
            Description = "Pagination offset.",
            DefaultValueFactory = _ => 0,
        };

        var traceIdOpt = new Option<string?>("--trace-id")
        {
            Description = "Only return log records whose trace_id equals this value.",
        };
        var serviceOpt = new Option<string?>("--service")
        {
            Description = "Only return log records whose service_name equals this value.",
        };
        var levelOpt = new Option<string?>("--level")
        {
            Description = "Only return log records whose severity equals this value (e.g. Error, Warning, Information). Maps to the OpenTelemetry `severity` column.",
        };
        var matchOpt = new Option<string?>("--match")
        {
            Description = "Full-text match across default full-text fields (match_all).",
        };
        var matchFieldOpt = new Option<string?>("--match-field")
        {
            Description = "Restrict pattern matching to this single field. Pair with --match-like/--match-regex/--match-glob.",
        };
        var matchLikeOpt = new Option<string?>("--match-like")
        {
            Description = "Match the --match-field column with a SQL LIKE pattern (% and _).",
        };
        var matchRegexOpt = new Option<string?>("--match-regex")
        {
            Description = "Match the --match-field column with a regex via re_match.",
        };
        var matchGlobOpt = new Option<string?>("--match-glob")
        {
            Description = "Match the --match-field column with a glob (* and ?).",
        };

        var command = new Command("logs", "Search logs. Emits a self-describing LogBundle.");
        command.Add(sinceOpt);
        command.Add(untilOpt);
        command.Add(sizeOpt);
        command.Add(fromOpt);
        command.Add(traceIdOpt);
        command.Add(serviceOpt);
        command.Add(levelOpt);
        command.Add(matchOpt);
        command.Add(matchFieldOpt);
        command.Add(matchLikeOpt);
        command.Add(matchRegexOpt);
        command.Add(matchGlobOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var since = parseResult.GetValue(sinceOpt);
            var until = parseResult.GetValue(untilOpt);
            var size = parseResult.GetValue(sizeOpt);
            var from = parseResult.GetValue(fromOpt);

            var filter = BuildLogsFilterSpec(
                traceId: parseResult.GetValue(traceIdOpt),
                service: parseResult.GetValue(serviceOpt),
                level: parseResult.GetValue(levelOpt),
                match: parseResult.GetValue(matchOpt),
                matchField: parseResult.GetValue(matchFieldOpt),
                matchLike: parseResult.GetValue(matchLikeOpt),
                matchRegex: parseResult.GetValue(matchRegexOpt),
                matchGlob: parseResult.GetValue(matchGlobOpt));

            var (startUs, endUs) = TimeRangeParser.Resolve(since, until, time, settings.LookbackMinutes);
            var lookbackMinutes = (int)((endUs - startUs) / (60L * 1_000_000L));

            var backend = backendFactory();
            try
            {
                var body = await backend.SearchLogsAsync(filter, startUs, endUs, from, size, ct);
                var hits = QueryCommand.ExtractHits(body);
                var logs = LogAssembler.Assemble(hits);

                var header = BundleBuilder.BuildHeader(
                    schema: LogBundle.CurrentSchema,
                    description: LogBundle.KeyConvention,
                    command: "logs",
                    backend: backend,
                    startTimeUs: startUs,
                    endTimeUs: endUs,
                    lookbackMinutes: lookbackMinutes,
                    queryInfo: new QueryInfo(
                        TraceId: filter.TraceId,
                        RequestedSize: size,
                        From: from,
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

    public static LogsFilterSpec BuildLogsFilterSpec(
        string? traceId,
        string? service,
        string? level,
        string? match,
        string? matchField,
        string? matchLike,
        string? matchRegex,
        string? matchGlob)
    {
        var modes = new[] { matchLike, matchRegex, matchGlob };
        var modeSet = modes.Count(s => s is not null);
        if (modeSet > 1)
            throw new InvalidOperationException(
                "--match-like, --match-regex and --match-glob are mutually exclusive. Pick one.");
        if (modeSet > 0 && matchField is null)
            throw new InvalidOperationException(
                "--match-like/--match-regex/--match-glob require --match-field.");
        if (matchField is not null && modeSet == 0)
            throw new InvalidOperationException(
                "--match-field requires one of --match-like/--match-regex/--match-glob.");
        if (match is not null && (matchField is not null || modeSet > 0))
            throw new InvalidOperationException(
                "--match and field-scoped pattern options are mutually exclusive.");

        OperationPattern? fieldMatch = null;
        if (matchField is not null)
        {
            var mode = matchLike is not null ? PatternMode.Like
                : matchRegex is not null ? PatternMode.Regex
                : PatternMode.Glob;
            var pattern = matchLike ?? matchRegex ?? matchGlob!;
            fieldMatch = new OperationPattern(pattern, mode);
        }

        return new LogsFilterSpec(
            TraceId: traceId,
            Service: service,
            Level: level,
            Match: match,
            MatchField: matchField,
            FieldMatch: fieldMatch);
    }
}
