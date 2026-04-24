using OpenTel.Query.Core.Processing;

namespace OpenTel.Query.Core.UnitTests;

public class StreamsAssemblerTests
{
    [Fact]
    public void ParseList_WithListWrapper_ReturnsEntries()
    {
        var body = """
            {
              "list": [
                {
                  "name": "default",
                  "stream_type": "logs",
                  "storage_type": "s3",
                  "stats": { "doc_time_min": 100, "doc_time_max": 200, "doc_num": 42, "storage_size": 3.5 },
                  "settings": { "partition_keys": {}, "full_text_search_keys": ["log", "message"] },
                  "schema": [ { "name": "_timestamp", "type": "Int64" }, { "name": "body", "type": "Utf8" } ]
                }
              ]
            }
            """;

        var streams = StreamsAssembler.ParseList(body);

        Assert.Single(streams);
        var s = streams[0];
        Assert.Equal("default", s.Name);
        Assert.Equal("logs", s.StreamType);
        Assert.Equal("s3", s.StorageType);
        Assert.Equal(100, s.Stats!.DocTimeMinUs);
        Assert.Equal(42, s.Stats.DocNum);
        Assert.Equal(3.5, s.Stats.StorageSize);
        Assert.Contains("log", s.Settings!.FullTextSearchKeys);
        Assert.Equal(2, s.Schema!.Count);
        Assert.Equal("body", s.Schema[1].Name);
        Assert.Equal("Utf8", s.Schema[1].Type);
    }

    [Fact]
    public void ParseList_WithoutSchema_ReturnsNullSchemaField()
    {
        var body = """
            {
              "list": [
                { "name": "x", "stream_type": "traces" }
              ]
            }
            """;

        var streams = StreamsAssembler.ParseList(body);

        Assert.Null(streams[0].Schema);
    }

    [Fact]
    public void ParseList_EmptyWrapper_ReturnsEmptyList()
    {
        Assert.Empty(StreamsAssembler.ParseList("""{"list":[]}"""));
    }

    [Fact]
    public void ParseList_RootIsArray_StillParses()
    {
        var body = """[ { "name": "x", "stream_type": "logs" } ]""";

        var streams = StreamsAssembler.ParseList(body);

        Assert.Single(streams);
        Assert.Equal("x", streams[0].Name);
    }

    [Fact]
    public void ParseSchemaOnly_ReadsSchemaArray()
    {
        var body = """
            {
              "name": "default",
              "stream_type": "logs",
              "schema": [
                { "name": "_timestamp", "type": "Int64" },
                { "name": "body", "type": "Utf8" }
              ]
            }
            """;

        var fields = StreamsAssembler.ParseSchemaOnly(body);

        Assert.Equal(2, fields.Count);
        Assert.Equal("_timestamp", fields[0].Name);
        Assert.Equal("Int64", fields[0].Type);
    }

    [Fact]
    public void ParseSettingsOnly_ReadsPartitionKeysFromObject()
    {
        var body = """
            {
              "settings": {
                "partition_keys": { "level1": "service_name" },
                "full_text_search_keys": ["message"]
              }
            }
            """;

        var settings = StreamsAssembler.ParseSettingsOnly(body);

        Assert.NotNull(settings);
        Assert.Contains("level1", settings!.PartitionKeys);
        Assert.Contains("message", settings.FullTextSearchKeys);
    }
}
