namespace EngineIO.Client;

/// <summary>
/// Represent EIO protocol packet types. see: https://socket.io/docs/v4/engine-io-protocol/#protocol
/// </summary>
public enum PacketType : byte
{
    Open = 0x30, // 0
    Close = 0x31, // 1
    Ping = 0x32,  // 2
    Pong = 0x33,  // 3
    Message = 0x34, // 4
    Upgrade = 0x35, // 5
    Noop = 0x36 // 6
}
