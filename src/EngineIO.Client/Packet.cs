namespace EngineIO.Client;

/// <summary>
///     Represent EIO protocol packet types. see: https://socket.io/docs/v4/engine-io-protocol/#protocol
/// </summary>
public enum PacketType : byte
{
    Open = 0x30,
    Close = 0x31,
    Ping = 0x32,
    Pong = 0x33,
    Message = 0x34,
    Upgrade = 0x35,
    Noop = 0x36
}
