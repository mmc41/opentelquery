using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace OpenTel.Query.Cli;

internal static class HelpCustomization
{
    public static void Configure(RootCommand root)
    {
        var helpOption = root.Options.OfType<HelpOption>().FirstOrDefault();
        if (helpOption is null) return;

        var originalAction = helpOption.Action;
        helpOption.Action = new ExtendedRootHelpAction(root, originalAction);
    }

    private sealed class ExtendedRootHelpAction : SynchronousCommandLineAction
    {
        private readonly RootCommand _root;
        private readonly CommandLineAction? _defaultAction;

        public ExtendedRootHelpAction(RootCommand root, CommandLineAction? defaultAction)
        {
            _root = root;
            _defaultAction = defaultAction;
        }

        public override int Invoke(ParseResult parseResult)
        {
            var invokedIsRoot = ReferenceEquals(parseResult.CommandResult.Command, _root);

            var result = _defaultAction switch
            {
                SynchronousCommandLineAction sync => sync.Invoke(parseResult),
                _ => 0,
            };

            if (!invokedIsRoot) return result;

            var output = parseResult.InvocationConfiguration.Output;
            WriteExamples(output);
            WritePerCommandDetails(output, parseResult);

            return result;
        }

        private static void WriteExamples(TextWriter w)
        {
            w.WriteLine();
            w.WriteLine("Examples:");
            w.WriteLine("  OpenTel.Query.Cli query --service Api --since \"15m ago\"");
            w.WriteLine("  OpenTel.Query.Cli query --op-like \"%Validate%\" --status ERROR");
            w.WriteLine("  OpenTel.Query.Cli query --duration-gt 500ms --http-status 5xx --since \"1h ago\"");
            w.WriteLine("  OpenTel.Query.Cli query --attr http.route=/api/statistik --since \"2h ago\"");
            w.WriteLine("  OpenTel.Query.Cli lookup <trace-id> --since \"1d ago\"");
            w.WriteLine("  OpenTel.Query.Cli logs --trace-id <trace-id>");
            w.WriteLine("  OpenTel.Query.Cli logs --level Error --match \"timeout\" --since \"6h ago\"");
            w.WriteLine("  OpenTel.Query.Cli logs --match-field body --match-regex \"E[0-9]{4}\"");
            w.WriteLine("  OpenTel.Query.Cli around --at 2026-04-23T13:38:00Z --size 20");
            w.WriteLine("  OpenTel.Query.Cli streams --type logs --fetch-schema");
            w.WriteLine("  OpenTel.Query.Cli schema xxx --type traces");
        }

        private void WritePerCommandDetails(TextWriter w, ParseResult rootResult)
        {
            w.WriteLine();
            w.WriteLine("Per-command details:");

            foreach (var sub in _root.Subcommands)
            {
                w.WriteLine();
                w.WriteLine(new string('─', 78));
                w.WriteLine($"{sub.Name} — {sub.Description}");
                w.WriteLine(new string('─', 78));

                var subParse = _root.Parse(new[] { sub.Name, "--help" });
                if (subParse.Action is SynchronousCommandLineAction subAction)
                    subAction.Invoke(subParse);
            }
        }
    }
}
