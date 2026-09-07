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
    ///     Parse a plain-text packet from a raw payload.
    /// </summary>
    /// <remarks>
    ///     Binary framing is transport-specific — base64 behind a 'b' prefix for
    ///     long-polling, a bare binary frame for WebSocket — so it is decoded by the
    ///     transport rather than here.
    /// </remarks>
    /// <param name="data">Buffer or raw payload</param>
    /// <param name="packet">Parsed Packet instance</param>
    /// <returns>Boolean indicating success or failure of parse operation</returns>
    public static bool TryParse(ReadOnlyMemory<byte> data, out Packet packet)
    {
        if (data.Length == 0)
        {
            packet = default;
            return false;
        }

        var type = (PacketType)data.Span[0];
        if (!Enum.IsDefined(type))
        {
            packet = default;
            return false;
        }

        packet = new Packet(PacketFormat.PlainText, type, data[1..]);
        return true;
    }

    public static Packet CreateMessagePacket(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return new Packet(PacketFormat.PlainText, PacketType.Message, body);
    }

    /// <summary>
    ///     Wrap an already UTF-8 encoded body in a plain-text message packet.
    /// </summary>
    /// <remarks>
    ///     A protocol layered on top of Engine.io — Socket.IO — encodes straight to
    ///     bytes, so routing it through <see cref="CreateMessagePacket(string)" />
    ///     would transcode the same payload twice for nothing.
    /// </remarks>
    public static Packet CreateMessagePacket(ReadOnlyMemory<byte> body)
    {
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