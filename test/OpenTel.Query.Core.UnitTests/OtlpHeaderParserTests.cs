using OpenTel.Query.Core.Configuration;

namespace OpenTel.Query.Core.UnitTests;

public class OtlpHeaderParserTests
{
    [Fact]
    public void ParsesThreeCommaSeparatedEntries()
    {
        var parsed = OtlpHeaderParser.Parse("Authorization=Basic abc, stream-name=default, organization=default");

        Assert.Equal("Basic abc", parsed["Authorization"]);
        Assert.Equal("default", parsed["stream-name"]);
        Assert.Equal("default", parsed["organization"]);
    }

    [Fact]
    public void PreservesBase64PaddingInValue()
    {
        var parsed = OtlpHeaderParser.Parse("Authorization=Basic bW1jOnNlY3JldA==, organization=default");

        Assert.Equal("Basic bW1jOnNlY3JldA==", parsed["Authorization"]);
    }

    [Fact]
    public void IsCaseInsensitiveOnKeys()
    {
        var parsed = OtlpHeaderParser.Parse("authorization=Basic x, Stream-Name=foo");

        Assert.Equal("Basic x", parsed["Authorization"]);
        Assert.Equal("foo", parsed["stream-name"]);
    }

    [Fact]
    public void IgnoresEmptyAndMalformedSegments()
    {
        var parsed = OtlpHeaderParser.Parse("Authorization=Basic x,,no-equals-here,organization=default");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Basic x", parsed["Authorization"]);
        Assert.Equal("default", parsed["organization"]);
    }
}
