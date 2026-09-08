using System;

namespace SocketIO.Client.Packets;

/// <summary>
///     A Socket.IO packet that arrived from the server.
///     see: https://socket.io/docs/v4/socket-io-protocol
/// </summary>
/// <remarks>
///     <para>
///         The counterpart of <see cref="PacketBuilder" />, which accumulates the
///         arguments of a packet to send. Two types rather than one because the two
///         sets of operations are never both valid, and because a merged type would
///         carry half its fields dead on every instance.
///     </para>
///     <para>
///         The payload is kept as the bytes it arrived in and read only when an
///         argument is asked for, so a packet nobody inspects costs no more than its
///         header. Holding the memory is safe: both transports copy a message out of
///         their receive buffer before handing it up, so nothing else owns it.
///     </para>
///     <para>
///         Indices address the data arguments alone. An event names itself in the
///         first element of the payload array, and that element is surfaced as
///         <see cref="Event" /> rather than as argument zero.
///     </para>
/// </remarks>
public sealed class Packet
{
    /// <summary>
    ///     The namespace every connection starts in, and the one a packet belongs to
    ///     when its header names none.
    /// </summary>
    public const string DefaultNamespace = "/";

    /// <summary>
    ///     Represents packet type.
    /// </summary>
    public PacketType Type => throw new NotImplementedException();

    /// <summary>
    ///     Namespace this packet belongs to, always in its leading-slash form.
    /// </summary>
    public string Namespace => throw new NotImplementedException();

    /// <summary>
    ///     Acknowledgement id this packet carries, when it takes part in one.
    /// </summary>
    /// <remarks>
    ///     On an event it is the id the server expects an acknowledgement under; on an
    ///     acknowledgement it is the id of the event being answered.
    /// </remarks>
    public int? AckId => throw new NotImplementedException();

    /// <summary>
    ///     Event name for the types that carry one, otherwise <c>null</c>.
    /// </summary>
    public string? Event => throw new NotImplementedException();

    /// <summary>
    ///     Number of data arguments, not counting the event name.
    /// </summary>
    public int Count => throw new NotImplementedException();

    /// <summary>
    ///     Parse the text part of a packet: everything up to and including the JSON
    ///     payload, but not the binary attachments it may announce.
    /// </summary>
    /// <remarks>
    ///     Deliberately unaware of attachments, so that parsing stays a pure function
    ///     of one buffer. Reassembling a packet that spans several Engine.io messages
    ///     is the <see cref="Decoder" />'s job, and keeping it there is what stops a
    ///     parse call from depending on the order it was made in.
    /// </remarks>
    /// <param name="data">The packet as it came off the wire</param>
    /// <param name="packet">Parsed packet instance</param>
    /// <returns>Boolean indicating success or failure of parse operation</returns>
    public static bool TryParse(ReadOnlyMemory<byte> data, out Packet? packet)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Whether the argument at <paramref name="index" /> arrived as a binary
    ///     attachment rather than as a Json value.
    /// </summary>
    public bool IsBinary(int index)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Deserialize the argument at <paramref name="index" />.
    /// </summary>
    /// <typeparam name="T">Type to deserialize the argument as</typeparam>
    public T? GetItem<T>(int index)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     The bytes of the argument at <paramref name="index" />, which has to be one
    ///     <see cref="IsBinary" /> reports as binary.
    /// </summary>
    public ReadOnlyMemory<byte> GetAttachment(int index)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     How many attachments the header announced.
    /// </summary>
    internal int AttachmentCount => throw new NotImplementedException();

    /// <summary>
    ///     Whether every attachment the header announced has arrived.
    /// </summary>
    /// <remarks>
    ///     True from the outset for a packet that announced none, which is every
    ///     packet that is not a binary one.
    /// </remarks>
    internal bool IsComplete => throw new NotImplementedException();

    /// <summary>
    ///     Take delivery of the next attachment, in the order the placeholders in the
    ///     payload refer to them.
    /// </summary>
    internal void Attach(ReadOnlyMemory<byte> attachment)
    {
        throw new NotImplementedException();
    }
}