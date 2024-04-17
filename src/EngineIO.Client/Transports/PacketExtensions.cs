using System;
using System.Text;
using EngineIO.Client.Packets;

namespace EngineIO.Client.Transports;

public static class PacketExtensions
{
    /// <summary>
    ///     Convert message packet to wire packet representation.
    /// </summary>
    /// <param name="packet">Packet instance</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static ReadOnlyMemory<byte> ToPlaintextPacket(this Packet packet)
    {
        if (packet.Format != PacketFormat.PlainText && packet.Type != PacketType.Message)
        {
            throw new InvalidOperationException("Wrong packet format");
        }

        var payload = new byte[1 + packet.Body.Length];
        payload[0] = (byte)packet.Type;
        for (var i = 1; i <= packet.Body.Length; i++)
        {
            payload[i] = packet.Body.Span[i - 1];
        }

        return payload;
    }

    /// <summary>
    ///     Convert message packet to wire packet representation.
    /// </summary>
    /// <param name="packet">Packet instance</param>
    /// <param name="encoder">Binary packet encoder</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static ReadOnlyMemory<byte> ToBinaryPacket(this Packet packet, IEncoder encoder)
    {
        if (packet.Format != PacketFormat.Binary && packet.Type != PacketType.Message)
        {
            throw new InvalidOperationException("Wrong packet format");
        }

        var encodedBody = encoder.Encode(packet.Body, Encoding.UTF8);
        var payload = new byte[1 + encodedBody.Length];
        payload[0] = (byte)'b';
        for (var i = 1; i <= encodedBody.Length; i++)
        {
            payload[i] = encodedBody.Span[i - 1];
        }

        return payload;
    }
}
