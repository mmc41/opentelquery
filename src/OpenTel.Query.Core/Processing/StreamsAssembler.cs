using System.Text.Json;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Core.Processing;

public static class StreamsAssembler
{
    public static IReadOnlyList<StreamInfo> ParseList(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var result = new List<StreamInfo>();

        var list = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("list", out var listEl) && listEl.ValueKind == JsonValueKind.Array
                ? listEl
                : default;

        if (list.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in list.EnumerateArray())
            result.Add(ParseEntry(entry));

        return result;
    }

    public static StreamInfo ParseEntry(JsonElement entry)
    {
        var name = ReadString(entry, "name") ?? "";
        var streamType = ReadString(entry, "stream_type") ?? "";
        var storageType = ReadString(entry, "storage_type");
        var stats = ParseStats(entry);
        var settings = ParseSettings(entry);
        var schema = ParseSchema(entry);

        return new StreamInfo(name, streamType, storageType, stats, settings, schema);
    }

    public static IReadOnlyList<FieldInfo> ParseSchemaOnly(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return ParseSchema(doc.RootElement) ?? Array.Empty<FieldInfo>();
    }

    public static StreamSettings? ParseSettingsOnly(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return ParseSettings(doc.RootElement);
    }

    private static StreamStats? ParseStats(JsonElement entry)
    {
        if (!entry.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
            return null;

        return new StreamStats(
            DocTimeMinUs: ReadLong(stats, "doc_time_min"),
            DocTimeMaxUs: ReadLong(stats, "doc_time_max"),
            DocNum: ReadLong(stats, "doc_num"),
            FileNum: ReadLong(stats, "file_num"),
            StorageSize: ReadDouble(stats, "storage_size"),
            CompressedSize: ReadDouble(stats, "compressed_size"));
    }

    private static StreamSettings? ParseSettings(JsonElement entry)
    {
        if (!entry.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
            return null;

        var partitionKeys = settings.TryGetProperty("partition_keys", out var pk)
            ? CollectKeys(pk)
            : Array.Empty<string>();

        var fullTextKeys = settings.TryGetProperty("full_text_search_keys", out var ft) && ft.ValueKind == JsonValueKind.Array
            ? ft.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList()
            : (IReadOnlyList<string>)Array.Empty<string>();

        return new StreamSettings(partitionKeys, fullTextKeys);
    }

    private static IReadOnlyList<string> CollectKeys(JsonElement element)
    {
        var keys = new List<string>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var str = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!string.IsNullOrEmpty(str)) keys.Add(str);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                keys.Add(prop.Name);
        }
        return keys;
    }

    private static IReadOnlyList<FieldInfo>? ParseSchema(JsonElement entry)
    {
        if (!entry.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Array)
            return null;

        var fields = new List<FieldInfo>();
        foreach (var field in schema.EnumerateArray())
        {
            var name = ReadString(field, "name") ?? "";
            var type = ReadString(field, "type") ?? "";
            fields.Add(new FieldInfo(name, type));
        }
        return fields;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64(),
            JsonValueKind.String when long.TryParse(value.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }
}
