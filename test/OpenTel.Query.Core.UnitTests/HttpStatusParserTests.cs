using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Core.UnitTests;

public class HttpStatusParserTests
{
    [Fact]
    public void Parse_ExactCode_ReturnsSingleRangeOfEqualBounds()
    {
        var spec = HttpStatusParser.Parse("404");

        Assert.Single(spec.Ranges);
        Assert.Equal((404, 404), spec.Ranges[0]);
    }

    [Fact]
    public void Parse_FiveXx_ReturnsRange500To599()
    {
        var spec = HttpStatusParser.Parse("5xx");

        Assert.Single(spec.Ranges);
        Assert.Equal((500, 599), spec.Ranges[0]);
    }

    [Fact]
    public void Parse_FourXx_ReturnsRange400To499()
    {
        var spec = HttpStatusParser.Parse("4XX");

        Assert.Equal((400, 499), spec.Ranges[0]);
    }

    [Fact]
    public void Parse_CommaSeparatedList_ReturnsMultipleRanges()
    {
        var spec = HttpStatusParser.Parse("404,500,5xx");

        Assert.Equal(3, spec.Ranges.Count);
        Assert.Equal((404, 404), spec.Ranges[0]);
        Assert.Equal((500, 500), spec.Ranges[1]);
        Assert.Equal((500, 599), spec.Ranges[2]);
    }

    [Fact]
    public void Parse_WhitespaceAroundParts_IsTrimmed()
    {
        var spec = HttpStatusParser.Parse(" 404 , 5xx ");

        Assert.Equal(2, spec.Ranges.Count);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => HttpStatusParser.Parse(""));
    }

    [Fact]
    public void Parse_CodeOutOfRange_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => HttpStatusParser.Parse("50"));
    }

    [Fact]
    public void Parse_ZeroXx_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => HttpStatusParser.Parse("0xx"));
    }

    [Fact]
    public void Parse_SixXx_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => HttpStatusParser.Parse("6xx"));
    }

    [Fact]
    public void Parse_NonNumericPart_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => HttpStatusParser.Parse("foo"));
    }
}
