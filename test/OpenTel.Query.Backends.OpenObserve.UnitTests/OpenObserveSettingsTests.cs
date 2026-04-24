using Microsoft.Extensions.Configuration;

namespace OpenTel.Query.Backends.OpenObserve.UnitTests;

public class OpenObserveSettingsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Load_ExtractsAuthorizationOrganizationAndStream()
    {
        var cfg = Build(new()
        {
            ["Telemetry:Headers"] = "Authorization=Basic token==, stream-name=default, organization=default",
        });

        var settings = OpenObserveSettings.Load(cfg);

        Assert.Equal("Basic token==", settings.Authorization);
        Assert.Equal("default", settings.Organization);
        Assert.Equal("default", settings.StreamName);
        Assert.Equal(new Uri("http://localhost:5080"), settings.Host);
    }

    [Fact]
    public void Load_HostOverrideWinsOverDefault()
    {
        var cfg = Build(new()
        {
            ["Telemetry:Headers"] = "Authorization=Basic x",
        });

        var settings = OpenObserveSettings.Load(cfg, hostOverride: "https://openobserve.internal");

        Assert.Equal(new Uri("https://openobserve.internal"), settings.Host);
    }

    [Fact]
    public void Load_DefaultsOrganizationAndStreamToDefault()
    {
        var cfg = Build(new()
        {
            ["Telemetry:Headers"] = "Authorization=Basic x",
        });

        var settings = OpenObserveSettings.Load(cfg);

        Assert.Equal("default", settings.Organization);
        Assert.Equal("default", settings.StreamName);
    }

    [Fact]
    public void Load_ThrowsWhenTelemetryHeadersMissing()
    {
        var cfg = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => OpenObserveSettings.Load(cfg));
        Assert.Contains("Telemetry:Headers", ex.Message);
    }

    [Fact]
    public void Load_ThrowsWhenAuthorizationMissingFromHeaders()
    {
        var cfg = Build(new()
        {
            ["Telemetry:Headers"] = "stream-name=default",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => OpenObserveSettings.Load(cfg));
        Assert.Contains("Authorization", ex.Message);
    }
}
