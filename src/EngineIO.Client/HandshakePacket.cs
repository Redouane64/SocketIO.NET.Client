using System.Text.Json.Serialization;

namespace EngineIO.Client;

/// <summary>
///     Represents handshake response payload.
/// </summary>
public class HandshakePacket
{
    [JsonPropertyName("sid")]
    public required string Sid { get; set; }

    [JsonPropertyName("upgrades")]
    public required string[] Upgrades { get; set; }

    [JsonPropertyName("pingInterval")]
    public required int PingInterval { get; set; }

    [JsonPropertyName("pingTimeout")]
    public required int PingTimeout { get; set; }

    [JsonPropertyName("maxPayload")]
    public required int MaxPayload { get; set; }
}
