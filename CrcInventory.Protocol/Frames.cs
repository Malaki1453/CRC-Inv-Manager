using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcInventory.Protocol;

/// <summary>Length-prefixed request envelope: protocol version, correlation id, op name, and JSON payload.</summary>
public sealed class WireRequest
{
    /// <summary>Protocol version the client believes it is speaking.</summary>
    [JsonPropertyName("v")]
    public int Version { get; set; } = ServerOps.ProtocolVersion;

    /// <summary>Correlation id copied onto the matching <see cref="WireResponse"/>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Named operation from <see cref="ServerOps"/>.</summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    /// <summary>Operation-specific JSON body.</summary>
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

/// <summary>Length-prefixed response envelope: success flag, optional payload, or an error string.</summary>
public sealed class WireResponse
{
    /// <summary>Protocol version the host is speaking.</summary>
    [JsonPropertyName("v")]
    public int Version { get; set; } = ServerOps.ProtocolVersion;

    /// <summary>Correlation id from the request this answers.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>True when the operation completed; false when <see cref="Error"/> is set.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>Success body; omitted on failure or when the op has no result.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>Public error text when <see cref="Ok"/> is false.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Shared System.Text.Json options for every framed request and response.</summary>
public static class JsonWire
{
    /// <summary>CamelCase, case-insensitive, omit nulls, compact (not indented) JSON.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
