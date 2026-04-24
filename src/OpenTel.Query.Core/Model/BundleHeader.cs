using System.Text.Json.Serialization;

namespace OpenTel.Query.Core.Model;

public record BundleHeader(
    [property: JsonPropertyName("$schema")][property: JsonPropertyOrder(-6)] string Schema,
    [property: JsonPropertyName("$description")][property: JsonPropertyOrder(-5)] string Description,
    [property: JsonPropertyOrder(-4)] string Command,
    [property: JsonPropertyOrder(-3)] SourceInfo Source,
    [property: JsonPropertyOrder(-2)] QueryWindow Window,
    [property: JsonPropertyOrder(-1)] QueryInfo QueryInfo);
