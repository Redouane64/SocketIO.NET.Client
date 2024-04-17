using System;
using System.Text;

namespace EngineIO.Client.Packets;

/// <summary>
///     Represent a message packet.
/// </summary>
public readonly struct Packet
{
    public static readonly Packet OpenPacket = new(PacketFormat.PlainText, PacketType.Open, Array.Empty<byte>());

    public static readonly Packet ClosePacket = new(PacketFormat.PlainText, PacketType.Close, Array.Empty<byte>());

    public static readonly Packet PongPacket = new(PacketFormat.PlainText, PacketType.Pong, Array.Empty<byte>());

    public static readonly Packet PingProbePacket = new(PacketFormat.PlainText, PacketType.Ping, new[]
    {
        (byte)'p', (byte)'r', (byte)'o', (byte)'b', (byte)'e'
    });

    public static readonly Packet UpgradePacket = new(PacketFormat.PlainText, PacketType.Upgrade, Array.Empty<byte>());

    /// <summary>
    ///     Parse packet from raw payload.
    /// </summary>
    /// <param name="data">Buffer or raw payload</param>
    /// <param name="packet">Parsed Packet instance</param>
    /// <returns>Boolean indicating success or failure of parse operation</returns>
    public static bool TryParse(ReadOnlyMemory<byte> data, out Packet packet)
    {
        var format = data.Span[0] == 98 ? PacketFormat.Binary : PacketFormat.PlainText;
        var type = format == PacketFormat.PlainText ? (PacketType)data.Span[0] : PacketType.Message;
        if (!Enum.IsDefined(typeof(PacketType), type))
        {
            packet = default;
            return false;
        }

        var content = data[1..];
        packet = new Packet(format, type, content);
        return true;
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
