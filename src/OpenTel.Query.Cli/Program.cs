using System.CommandLine;
using Microsoft.Extensions.Configuration;
using OpenTel.Query.Cli;
using OpenTel.Query.Core.Abstractions;
using OpenTel.Query.Core.Configuration;

try
{
    var bootstrap = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    var cfgBuilder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false);

    var secretsId = bootstrap["UserSecretsId"];
    if (!string.IsNullOrWhiteSpace(secretsId))
        cfgBuilder.AddUserSecrets(secretsId);

    cfgBuilder.AddEnvironmentVariables();
    var cfg = cfgBuilder.Build();

    var settings = QuerySettings.Load(cfg);
    Func<ITelemetryBackend> backendFactory = () => BackendFactory.Create(cfg);

    var root = CliBuilder.Build(backendFactory, settings, TimeProvider.System, Console.Out);
    var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    return await root.Parse(args).InvokeAsync(invocation);
}
catch (TelemetryBackendException ex)
{
    Console.Error.WriteLine(ex.Message);
    if (ex.Hint is not null) Console.Error.WriteLine(ex.Hint);
    if (!string.IsNullOrWhiteSpace(ex.ResponseBody)) Console.Error.WriteLine(ex.ResponseBody);
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Network error: {ex.Message}");
    return 3;
}

public partial class Program;
