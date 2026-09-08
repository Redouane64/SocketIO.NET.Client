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

/// <summary>
///     What each packet type is allowed to carry.
/// </summary>
/// <remarks>
///     The rules belong to the type rather than to either packet class, so that the
///     builder and the parser cannot drift apart on what a header may hold.
/// </remarks>
internal static class PacketTypeExtensions
{
    /// <summary>
    ///     Whether the first payload argument is an event name.
    /// </summary>
    public static bool CarriesEventName(this PacketType type)
    {
        return type is PacketType.Event or PacketType.BinaryEvent;
    }

    /// <summary>
    ///     Whether the header may hold an acknowledgement id.
    /// </summary>
    public static bool CarriesAckId(this PacketType type)
    {
        return type is PacketType.Event or PacketType.Ack or PacketType.BinaryEvent or PacketType.BinaryAck;
    }

    /// <summary>
    ///     Whether the packet announces attachments and is followed by them.
    /// </summary>
    public static bool CarriesAttachments(this PacketType type)
    {
        return type is PacketType.BinaryEvent or PacketType.BinaryAck;
    }
}