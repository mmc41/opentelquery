using Microsoft.Extensions.Configuration;
using OpenTel.Query.Core.Configuration;

namespace OpenTel.Query.Backends.OpenObserve;

public sealed record OpenObserveSettings(
    Uri Host,
    string Authorization,
    string Organization,
    string StreamName)
{
    private const string DefaultHost = "http://localhost:5080";

    public static OpenObserveSettings Load(IConfiguration cfg, string? hostOverride = null)
    {
        var headerString = cfg["Telemetry:Headers"]
            ?? throw new InvalidOperationException(
                "Telemetry:Headers not found. Set it in user secrets "
                + "(id configured via `UserSecretsId` in appsettings.json) "
                + "or via the Telemetry__Headers environment variable.");
        var parsed = OtlpHeaderParser.Parse(headerString);

        var authorization = parsed.GetValueOrDefault("Authorization")
            ?? throw new InvalidOperationException("Telemetry:Headers does not contain an Authorization entry.");
        var organization = parsed.GetValueOrDefault("organization", "default");
        var streamName = parsed.GetValueOrDefault("stream-name", "default");

        var hostString = hostOverride ?? cfg["OpenObserve:Host"] ?? DefaultHost;
        if (!Uri.TryCreate(hostString, UriKind.Absolute, out var host))
            throw new InvalidOperationException($"Invalid host URI: '{hostString}'.");

        return new OpenObserveSettings(host, authorization, organization, streamName);
    }
}
