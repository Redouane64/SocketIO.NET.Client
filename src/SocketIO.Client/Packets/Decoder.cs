using System;

using EngineIO.Client.Packets;

using SocketIO.Client.Exceptions;

using EnginePacket = EngineIO.Client.Packets.Packet;

namespace SocketIO.Client.Packets;

/// <summary>
///     Turns a stream of Engine.io messages into Socket.IO packets.
/// </summary>
/// <remarks>
///     <para>
///         A text packet is one Engine.io message and is done when it is parsed. A
///         binary one is a header followed by exactly as many binary messages as it
///         announced, so the decoder holds it back until they have all arrived. That
///         waiting is the whole reason this type exists rather than the work living in
///         <see cref="Packet.TryParse" />, which stays a pure function of one buffer.
///     </para>
///     <para>
///         Both errors it raises are the ones the reference decoder raises, and they
///         mean the stream is no longer trustworthy: the run of messages that make up
///         a packet has been broken into. Neither is recoverable by skipping a packet.
///     </para>
/// </remarks>
internal sealed class Decoder
{
    /// <summary>
    ///     The header still owed attachments, if any.
    /// </summary>
    private Packet? _pending;

    /// <summary>
    ///     Whether a packet is part-way through arriving.
    /// </summary>
    public bool IsReconstructing => _pending is not null;

    /// <summary>
    ///     Take the next Engine.io message.
    /// </summary>
    /// <param name="message">An Engine.io message packet</param>
    /// <returns>
    ///     The packet, once it is whole, or <c>null</c> while one is still arriving.
    /// </returns>
    public Packet? Add(EnginePacket message)
    {
        return message.Format == PacketFormat.Binary
            ? AddAttachment(message.Body)
            : AddHeader(message.Body);
    }

    private Packet? AddHeader(ReadOnlyMemory<byte> data)
    {
        if (_pending is not null)
        {
            throw new PacketFormatException(
                "A packet header arrived while another was still waiting for its attachments.");
        }

        if (!Packet.TryParse(data, out var packet))
        {
            throw new PacketFormatException("The packet could not be decoded.");
        }

        // A packet that announced no attachments is whole as soon as it is parsed,
        // which is every packet that is not a binary one.
        if (packet!.IsComplete)
        {
            return packet;
        }

        _pending = packet;
        return null;
    }

    private Packet? AddAttachment(ReadOnlyMemory<byte> data)
    {
        if (_pending is null)
        {
            throw new PacketFormatException("A binary attachment arrived with no packet waiting for one.");
        }

        _pending.Attach(data);

        if (!_pending.IsComplete)
        {
            return null;
        }

        var completed = _pending;
        _pending = null;
        return completed;
    }
}