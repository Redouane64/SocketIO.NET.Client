namespace SocketIO.Client.Packets;

/// <summary>
///     Represent Socket.IO protocol packet types. see: https://socket.io/docs/v4/socket-io-protocol
/// </summary>
/// <remarks>
///     As with <see cref="EngineIO.Client.Packets.PacketType" />, each value is the
///     ASCII byte of the digit it is written as on the wire, so encoding a type is a
///     cast rather than a lookup.
/// </remarks>
public enum PacketType : byte
{
    /// <summary>
    ///     Connect packet type.
    /// </summary>
    Connect = 0x30,

    /// <summary>
    ///     Disconnect packet type.
    /// </summary>
    Disconnect = 0x31,

    /// <summary>
    ///     Event packet type with plain text/JSON data.
    /// </summary>
    Event = 0x32,

    /// <summary>
    ///     Acknowledgement packet type with plain text/JSON data.
    /// </summary>
    Ack = 0x33,

    /// <summary>
    ///     Connection error packet type.
    /// </summary>
    ConnectError = 0x34,

    /// <summary>
    ///     Event packet type with binary data.
    /// </summary>
    BinaryEvent = 0x35,

    /// <summary>
    ///     Acknowledgement packet type with binary data.
    /// </summary>
    BinaryAck = 0x36
}