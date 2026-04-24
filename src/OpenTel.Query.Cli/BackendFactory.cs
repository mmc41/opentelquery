using Microsoft.Extensions.Configuration;
using OpenTel.Query.Backends.OpenObserve;
using OpenTel.Query.Core.Abstractions;

namespace OpenTel.Query.Cli;

internal static class BackendFactory
{
    public const string DefaultBackend = OpenObserveBackend.Name;

    public static ITelemetryBackend Create(IConfiguration cfg)
    {
        var name = cfg["Backend"] ?? DefaultBackend;
        return name.ToLowerInvariant() switch
        {
            OpenObserveBackend.Name => OpenObserveBackend.Create(cfg),
            _ => throw new InvalidOperationException(
                $"Unknown backend '{name}'. Supported backends: {OpenObserveBackend.Name}."),
        };
    }
}
