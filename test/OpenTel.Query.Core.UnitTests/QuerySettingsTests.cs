using Microsoft.Extensions.Configuration;
using OpenTel.Query.Core.Configuration;

namespace OpenTel.Query.Core.UnitTests;

public class QuerySettingsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Load_ReadsLookbackMinutes()
    {
        var cfg = Build(new() { ["Query:LookbackMinutes"] = "400" });

        var settings = QuerySettings.Load(cfg);

        Assert.Equal(400, settings.LookbackMinutes);
    }

    [Fact]
    public void Load_ThrowsWhenMissing()
    {
        var cfg = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => QuerySettings.Load(cfg));
        Assert.Contains("Query:LookbackMinutes", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public void Load_ThrowsWhenInvalid(string raw)
    {
        var cfg = Build(new() { ["Query:LookbackMinutes"] = raw });

        var ex = Assert.Throws<InvalidOperationException>(() => QuerySettings.Load(cfg));
        Assert.Contains("positive integer", ex.Message);
    }
}
