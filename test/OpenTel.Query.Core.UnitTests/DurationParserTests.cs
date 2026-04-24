using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Core.UnitTests;

public class DurationParserTests
{
    [Theory]
    [InlineData("500us", 500L)]
    [InlineData("1000us", 1_000L)]
    [InlineData("1ms", 1_000L)]
    [InlineData("250ms", 250_000L)]
    [InlineData("1s", 1_000_000L)]
    [InlineData("2s", 2_000_000L)]
    [InlineData("3m", 180_000_000L)]
    [InlineData("1h", 3_600_000_000L)]
    public void ParseToMicroseconds_KnownFormats_ReturnCorrectUs(string input, long expectedUs)
    {
        Assert.Equal(expectedUs, DurationParser.ParseToMicroseconds(input));
    }

    [Fact]
    public void ParseToMicroseconds_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DurationParser.ParseToMicroseconds(""));
    }

    [Fact]
    public void ParseToMicroseconds_NoUnit_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DurationParser.ParseToMicroseconds("500"));
    }

    [Fact]
    public void ParseToMicroseconds_UnknownUnit_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DurationParser.ParseToMicroseconds("5d"));
    }

    [Fact]
    public void ParseToMicroseconds_NonNumeric_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DurationParser.ParseToMicroseconds("abcms"));
    }

    [Fact]
    public void ParseToMicroseconds_NegativeValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DurationParser.ParseToMicroseconds("-1s"));
    }

    [Fact]
    public void ParseToMicroseconds_FractionalSeconds_Rounds()
    {
        Assert.Equal(1_500_000L, DurationParser.ParseToMicroseconds("1.5s"));
    }
}
