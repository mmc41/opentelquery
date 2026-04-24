using System.CommandLine;

namespace OpenTel.Query.Cli.Commands;

internal static class TimeRangeOptions
{
    public static (Option<string?> Since, Option<string?> Until) Create()
    {
        var sinceOpt = new Option<string?>("--since")
        {
            Description = "Start of the window. ISO-8601 (2026-04-20T14:00Z) or relative (15m ago, 2h ago, 3d ago). Default: Query:LookbackMinutes before --until.",
        };
        var untilOpt = new Option<string?>("--until")
        {
            Description = "End of the window. ISO-8601 or relative. Default: now.",
        };
        return (sinceOpt, untilOpt);
    }
}
