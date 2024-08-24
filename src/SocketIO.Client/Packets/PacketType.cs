using System;

namespace SocketIO.Client.Packets;

public enum PacketType : byte
{
    Connect = (byte)'0',
    Disconnect = (byte)'1',
    Event = (byte)'2',
    Ack = (byte)'3',
    ConnectError = (byte)'4',
    BinaryEvent = (byte)'5',
    BinaryAck = (byte)'6'
}

/**
 * SocketIO packet format:
 * 
 * <packet type>[<# of binary attachments>-][<namespace>,][<acknowledgment id>][JSON-stringified payload without binary]
 *  + binary attachments extracted
 * 
 */

public struct Packet
{
    public static Packet ConnectPacket = new(PacketType.Connect, Array.Empty<byte>());

    public static Packet DisconnectPacket = new(PacketType.Disconnect, Array.Empty<byte>());

    public PacketType Type { get; private set; }
    public string? Namespace { get; private set; }
    public ReadOnlyMemory<byte> Data { get; private set; }
    public uint Id { get; private set; }

    public Packet(PacketType type, ReadOnlyMemory<byte> data)
    {
        Type = type;
        Data = data;
    }
    
    public Packet(PacketType type, string @namespace, ReadOnlyMemory<byte> data)
        : this(type, data)
    {
        Namespace = @namespace;
    }
    
    public Packet(PacketType type, string @namespace, ReadOnlyMemory<byte> data, uint id)
        : this(type, @namespace, data)
    {
        Id = id;
    }
    
    public Packet(PacketType type, ReadOnlyMemory<byte> data, uint id)
        : this(type, data)
    {
        Id = id;
    }
}