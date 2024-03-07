namespace EngineIO.Client;

/// <summary>
///     Represent EIO protocol packet types. see: https://socket.io/docs/v4/engine-io-protocol/#protocol
/// </summary>
public enum PacketType : byte
{
    /// <summary>
    /// Packet type 0
    /// </summary>
    Open = 0x30,
    /// <summary>
    /// Packet type 1
    /// </summary>
    Close = 0x31,
    /// <summary>
    /// Packet type 2
    /// </summary>
    Ping = 0x32,
    /// <summary>
    /// Packet type 3
    /// </summary>
    Pong = 0x33,
    /// <summary>
    /// Packet type 4
    /// </summary>
    Message = 0x34,
    /// <summary>
    /// Packet type 5
    /// </summary>
    Upgrade = 0x35,
    /// <summary>
    /// Packet type 6
    /// </summary>
    Noop = 0x36
}
