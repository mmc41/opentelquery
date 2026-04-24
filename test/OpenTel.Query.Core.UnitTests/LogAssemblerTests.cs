using System.Text.Json;
using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Core.UnitTests;

public class LogAssemblerTests
{
    [Fact]
    public void Assemble_SeparatesProcessFromAttributes()
    {
        var hit = Parse("""
            {
              "_timestamp": 1700000000000000,
              "trace_id": "t1",
              "span_id": "s1",
              "service_name": "Api",
              "service_instance_id": "pod-1",
              "level": "info",
              "message": "hello",
              "user_id": "42"
            }
            """);

        var log = LogAssembler.Assemble(new[] { hit })[0];

        Assert.Equal("t1", log.TraceId);
        Assert.Equal("s1", log.SpanId);
        Assert.Equal("info", log.Level);
        Assert.Equal("Api", log.Service);
        Assert.Equal("hello", log.Message);

        Assert.Equal("Api", log.Process["service_name"]);
        Assert.Equal("pod-1", log.Process["service_instance_id"]);

        Assert.True(log.Attributes.ContainsKey("user_id"));
        Assert.False(log.Attributes.ContainsKey("service_name"));
    }

    [Fact]
    public void Assemble_FallsBackToLogFieldForMessage()
    {
        var hit = Parse("""
            {
              "_timestamp": 1700000000000000,
              "log": "body text"
            }
            """);

        var log = LogAssembler.Assemble(new[] { hit })[0];
        Assert.Equal("body text", log.Message);
    }

    [Fact]
    public void Assemble_SupportsMessageAliases()
    {
        var hit = Parse("""
            {
              "_timestamp": 1700000000000000,
              "msg": "body"
            }
            """);

        Assert.Equal("body", LogAssembler.Assemble(new[] { hit })[0].Message);
    }

    [Fact]
    public void Assemble_AcceptsSeverityTextAsLevelAlias()
    {
        var hit = Parse("""
            {
              "_timestamp": 1700000000000000,
              "severity_text": "warn"
            }
            """);

        Assert.Equal("warn", LogAssembler.Assemble(new[] { hit })[0].Level);
    }

    [Fact]
    public void Assemble_OrdersRecordsByTimestamp()
    {
        var hitA = Parse("""{ "_timestamp": 2000000000000000, "message": "later" }""");
        var hitB = Parse("""{ "_timestamp": 1000000000000000, "message": "earlier" }""");

        var logs = LogAssembler.Assemble(new[] { hitA, hitB });

        Assert.Equal("earlier", logs[0].Message);
        Assert.Equal("later", logs[1].Message);
    }

    [Fact]
    public void Assemble_MissingOptionalFields_NullsAreReturned()
    {
        var hit = Parse("""
            {
              "_timestamp": 1700000000000000,
              "message": "bare"
            }
            """);

        var log = LogAssembler.Assemble(new[] { hit })[0];
        Assert.Null(log.TraceId);
        Assert.Null(log.SpanId);
        Assert.Null(log.Service);
        Assert.Null(log.Level);
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
