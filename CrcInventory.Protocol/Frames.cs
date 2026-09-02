using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcInventory.Protocol;

public sealed class WireRequest
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = ServerOps.ProtocolVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public sealed class WireResponse
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = ServerOps.ProtocolVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public static class JsonWire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
