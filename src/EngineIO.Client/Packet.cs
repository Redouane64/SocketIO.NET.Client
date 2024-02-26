using System.Text;

namespace EngineIO.Client;

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
    public static Packet Pong()
    {
        return new Packet([(byte)PacketType.Pong]);
    }
    
    public static Packet ClosePacket()
    {
        return new Packet([(byte)PacketType.Close]);
    }

    private string? _text;

    public Packet(byte[] data)
    {
        Data = data;
    }

    public byte[] Data { get; }

    public PacketType Type => (PacketType)Data[0];
    public byte[] Payload => Data.AsSpan(1..).ToArray();

    public override string ToString()
    {
        if (_text is not null) return _text;

        _text = Encoding.UTF8.GetString(Payload);
        return _text;
    }
}