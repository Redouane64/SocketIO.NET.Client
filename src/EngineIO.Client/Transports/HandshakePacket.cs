using System.Text.Json.Serialization;

namespace EngineIO.Client.Transports;

/// <summary>
///     Payload of the Engine.IO open packet.
/// </summary>
internal sealed class HandshakePacket
{
    [JsonPropertyName("sid")]
    public string? Sid { get; set; }

    [JsonPropertyName("upgrades")]
    public string[]? Upgrades { get; set; }

    [JsonPropertyName("pingInterval")]
    public int PingInterval { get; set; }

    [JsonPropertyName("pingTimeout")]
    public int PingTimeout { get; set; }

    [JsonPropertyName("maxPayload")]
    public int MaxPayload { get; set; }
}

/// <summary>
///     Source-generated metadata for <see cref="HandshakePacket" />. Reflection-based
///     serialization is unavailable to trimmed and Native AOT applications.
/// </summary>
[JsonSerializable(typeof(HandshakePacket))]
internal sealed partial class HandshakeJsonContext : JsonSerializerContext
{
}