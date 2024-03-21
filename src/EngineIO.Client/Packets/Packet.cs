using System.Text;

namespace EngineIO.Client.Packets;

/// <summary>
///     Represent a message packet.
/// </summary>
public readonly struct Packet
{
    public static readonly Packet OpenPacket = Parse(new[] { (byte)PacketType.Open });

    public static readonly Packet ClosePacket = Parse(new[] { (byte)PacketType.Close });

    public static readonly Packet PongPacket = Parse(new[] { (byte)PacketType.Pong });

    public static readonly Packet PingProbePacket = Parse(new[]
    {
        (byte)PacketType.Ping, (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e'
    });

    public static readonly Packet UpgradePacket = Parse(new byte[1] { (byte)PacketType.Upgrade });

    /// <summary>
    ///     Parse packet from raw payload.
    /// </summary>
    /// <param name="packet">Packet payload</param>
    /// <returns>Packet instance</returns>
    public static Packet Parse(ReadOnlyMemory<byte> packet)
    {
        var format = packet.Span[0] == 98 ? PacketFormat.Binary : PacketFormat.PlainText;
        var type = format == PacketFormat.PlainText ? (PacketType)packet.Span[0] : PacketType.Message;
        var content = packet[1..];

        return new Packet(format, type, content);
    }

    public static Packet CreateMessagePacket(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return new Packet(PacketFormat.PlainText, PacketType.Message, body);
    }

    public static Packet CreateBinaryPacket(ReadOnlyMemory<byte> body)
    {
        return new Packet(PacketFormat.Binary, PacketType.Message, body);
    }

    public Packet(PacketFormat format, PacketType type, ReadOnlyMemory<byte> body)
    {
        Format = format;
        Type = format == PacketFormat.Binary ? PacketType.Message : type;
        Body = body;
        Length = body.Length + 1;
    }

    /// <summary>
    ///     Represent packet payload size including the type byte.
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     Represents packet type.
    /// </summary>
    public PacketType Type { get; }

    /// <summary>
    ///     Indicate content format stored in the packet.
    /// </summary>
    public PacketFormat Format { get; }

    /// <summary>
    ///     Packet body excluding packet type.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; }
}
