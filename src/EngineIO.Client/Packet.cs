using System.Text;

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

public struct Packet
{
    private static readonly Packet _pongPacket = new([(byte)PacketType.Pong]);
    private static readonly Packet _closePacket = new([(byte)PacketType.Close]);

    /// <summary>
    ///     Create Pong packet.
    /// </summary>
    /// <returns>Packet of type Pong</returns>
    public static Packet PongPacket()
    {
        return _pongPacket;
    }

    /// <summary>
    ///     Create Close packet.
    /// </summary>
    /// <returns>Packet of type Close</returns>
    public static Packet ClosePacket()
    {
        return _closePacket;
    }

    private string _text;

    /// <summary>
    ///     Decode packet payload using Encoding.UTF8.
    /// </summary>
    public string? Text()
    {
        if (Payload.Length > 0) return null;

        if (_text is not null) return _text;

        _text = Encoding.UTF8.GetString(Payload);
        return _text;
    }

    public Packet(byte[] data)
    {
        Data = data;
    }

    /// <summary>
    ///     Packet raw payload, including packet type flag.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    ///     Packet type.
    /// </summary>
    public PacketType Type => (PacketType)Data[0];

    /// <summary>
    ///     Packet payload without type flag.
    /// </summary>
    public byte[] Payload => Data.AsSpan(1..).ToArray();
}