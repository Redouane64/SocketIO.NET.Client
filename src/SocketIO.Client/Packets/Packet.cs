using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SocketIO.Client.Packets;

/// <summary>
///     Represent a Socket.IO packet. see: https://socket.io/docs/v4/socket-io-protocol
/// </summary>
/// <remarks>
///     <para>
///         The wire format is a header followed by a JSON payload:
///         <c>&lt;type&gt;[&lt;# of binary attachments&gt;-][&lt;namespace&gt;,][&lt;ack id&gt;]&lt;JSON payload&gt;</c>.
///     </para>
///     <para>
///         Binary arguments never appear in that JSON. Each leaves a placeholder behind
///         and travels as its own packet after the header — see <see cref="Attachments" />.
///     </para>
/// </remarks>
public sealed class Packet
{
    /// <summary>
    ///     The namespace every connection starts in.
    /// </summary>
    public const string DefaultNamespace = "/";

    /// <summary>
    ///     The event name used when the caller does not name one.
    /// </summary>
    public const string DefaultEventName = "message";

    public static readonly Packet ConnectPacket = new(PacketType.Connect);

    public static readonly Packet DisconnectPacket = new(PacketType.Disconnect);

    private readonly List<IPacketData> _data = new();

    private readonly List<ReadOnlyMemory<byte>> _attachments = new();

    public Packet(PacketType type)
        : this(type, null, null, null)
    {
    }

    public Packet(PacketType type, string? @namespace)
        : this(type, @namespace, null, null)
    {
    }

    public Packet(PacketType type, string? @namespace, string? @event)
        : this(type, @namespace, @event, null)
    {
    }

    public Packet(PacketType type, int ackId, string? @namespace, string? @event)
        : this(type, @namespace, @event, ackId)
    {
    }

    /// <summary>
    ///     The one constructor that validates, so that no combination reaches the
    ///     wire without having been checked.
    /// </summary>
    private Packet(PacketType type, string? @namespace, string? @event, int? ackId)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown packet type.");
        }

        if (@event is not null && !CarriesEventName(type))
        {
            throw new ArgumentException($"A {type} packet does not carry an event name.", nameof(@event));
        }

        if (ackId.HasValue && !CarriesAckId(type))
        {
            throw new ArgumentException($"A {type} packet cannot carry an acknowledgement id.", nameof(type));
        }

        if (ackId is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ackId), ackId,
                "An acknowledgement id is a non-negative number; a sign would be read as the start of the payload.");
        }

        // An acknowledgement that names no id answers nothing: the server looks the id
        // up among the callbacks it is waiting on and discards the packet when it is
        // missing, so it is refused here rather than sent into the void.
        if (!ackId.HasValue && type is PacketType.Ack or PacketType.BinaryAck)
        {
            throw new ArgumentException($"A {type} packet has to name the acknowledgement it answers.",
                nameof(ackId));
        }

        Type = type;
        Namespace = NormalizeNamespace(@namespace);
        AckId = ackId;

        if (!CarriesEventName(type))
        {
            return;
        }

        // The event name is not header material: it is the first argument of the
        // payload array, which is why it is seeded as an item like any other.
        Event = @event ?? DefaultEventName;

        if (IsReservedEventName(Event))
        {
            throw new ArgumentException(
                $"\"{Event}\" is reserved by the protocol; a packet naming it is rejected by the server.",
                nameof(@event));
        }

        _data.Add(new TextPacketData(Event));
    }

    /// <summary>
    ///     Represents packet type.
    /// </summary>
    public PacketType Type { get; }

    /// <summary>
    ///     Namespace this packet belongs to, always in its leading-slash form.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    ///     Acknowledgement id correlating an event with its acknowledgement, when the
    ///     packet takes part in one.
    /// </summary>
    public int? AckId { get; }

    /// <summary>
    ///     Event name for the types that carry one, otherwise <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     An acknowledgement answers an event rather than naming one, so its payload
    ///     holds the response arguments alone.
    /// </remarks>
    public string? Event { get; }

    /// <summary>
    ///     The binary arguments, in the order their placeholders reference them. Each
    ///     is sent as a separate binary packet after this one.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Attachments => _attachments;

    /// <summary>
    ///     Add plain text data to packet.
    /// </summary>
    /// <param name="data">Plain text data</param>
    public void AddItem(string data)
    {
        AddPacketData(new TextPacketData(data));
    }

    /// <summary>
    ///     Add Json serializable POCO.
    /// </summary>
    /// <param name="data">Data instance</param>
    /// <typeparam name="T">Data type</typeparam>
    public void AddItem<T>(T data) where T : class
    {
        AddPacketData(new JsonPacketData<T>(data));
    }

    /// <summary>
    ///     Add binary data.
    /// </summary>
    /// <remarks>
    ///     Present so that a byte array reaches the binary overload rather than
    ///     <see cref="AddItem{T}" />, which would quietly encode it as a base64 string.
    /// </remarks>
    /// <param name="data">Binary data</param>
    public void AddItem(byte[] data)
    {
        AddItem(new ReadOnlyMemory<byte>(data));
    }

    /// <summary>
    ///     Add binary data.
    /// </summary>
    /// <param name="data">Binary data</param>
    public void AddItem(ReadOnlyMemory<byte> data)
    {
        if (Type is not (PacketType.BinaryEvent or PacketType.BinaryAck))
        {
            throw new InvalidOperationException(
                $"A {Type} packet cannot carry binary data; use {nameof(PacketType.BinaryEvent)} " +
                $"or {nameof(PacketType.BinaryAck)}.");
        }

        AddPacketData(new BinaryPacketData(_attachments.Count, data));
        _attachments.Add(data);
    }

    /// <summary>
    ///     Append an argument to the payload.
    /// </summary>
    /// <remarks>
    ///     Named apart from the public <c>AddItem</c> overloads on purpose: an
    ///     <see cref="IPacketData" /> is a class, so an overload by that name would
    ///     bind to <see cref="AddItem{T}" /> and recurse into itself.
    /// </remarks>
    private void AddPacketData(IPacketData data)
    {
        if (Type is PacketType.Connect or PacketType.Disconnect)
        {
            throw new InvalidOperationException($"A {Type} packet does not carry a payload.");
        }

        _data.Add(data);
    }

    /// <summary>
    ///     Serialize the packet header and payload to their wire representation.
    /// </summary>
    /// <remarks>
    ///     The result is the text part only. Anything in <see cref="Attachments" />
    ///     follows it as separate packets.
    /// </remarks>
    /// <returns>The encoded packet</returns>
    internal ReadOnlyMemory<byte> Serialize()
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serialize(buffer);
        return buffer.WrittenMemory;
    }

    /// <summary>
    ///     Serialize the packet into a caller-owned buffer.
    /// </summary>
    /// <remarks>
    ///     Writing rather than returning keeps the packet stateless, so the same
    ///     instance — <see cref="ConnectPacket" /> among them — can be sent repeatedly.
    /// </remarks>
    internal void Serialize(IBufferWriter<byte> writer)
    {
        // A decoder reads the announced count and refuses anything below one, so a
        // binary packet with nothing attached is not an empty packet — it is one the
        // server drops the connection over. It is caught here rather than in the
        // constructor because the attachments arrive after it.
        if ((Type is PacketType.BinaryEvent or PacketType.BinaryAck) && _attachments.Count == 0)
        {
            throw new InvalidOperationException(
                $"A {Type} packet has to carry at least one binary argument; " +
                $"use {nameof(PacketType.Event)} or {nameof(PacketType.Ack)} for a payload that has none.");
        }

        WriteHeader(writer);
        WritePayload(writer);
    }

    private void WriteHeader(IBufferWriter<byte> writer)
    {
        WriteByte(writer, (byte)Type);

        // The count and its dash are what mark a type 5 or 6 packet as binary, so they
        // are written for the type rather than for the attachments happening to be
        // there — Serialize has already refused the packet if they are not.
        if (Type is PacketType.BinaryEvent or PacketType.BinaryAck)
        {
            WriteInt32(writer, _attachments.Count);
            WriteByte(writer, (byte)'-');
        }

        // The default namespace is implied by its absence.
        if (!string.Equals(Namespace, DefaultNamespace, StringComparison.Ordinal))
        {
            var length = Encoding.UTF8.GetByteCount(Namespace);
            var span = writer.GetSpan(length + 1);
            Encoding.UTF8.GetBytes(Namespace, span);
            span[length] = (byte)',';
            writer.Advance(length + 1);
        }

        if (AckId.HasValue)
        {
            WriteInt32(writer, AckId.Value);
        }
    }

    private void WritePayload(IBufferWriter<byte> writer)
    {
        if (Type is PacketType.Connect or PacketType.Disconnect)
        {
            return;
        }

        using var json = new Utf8JsonWriter(writer);

        // CONNECT_ERROR is the one payload that is not an argument list: it is the
        // error object on its own.
        if (Type == PacketType.ConnectError)
        {
            if (_data.Count > 0)
            {
                _data[0].Serialize(json);
            }

            json.Flush();
            return;
        }

        json.WriteStartArray();

        foreach (var item in _data)
        {
            item.Serialize(json);
        }

        json.WriteEndArray();
        json.Flush();
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    private static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        // Room for every digit an int can produce, sign included.
        var span = writer.GetSpan(11);
        value.TryFormat(span, out var written);
        writer.Advance(written);
    }

    /// <summary>
    ///     Bring a namespace to the single form the wire uses, so that "admin",
    ///     "/admin" and a missing namespace do not encode three different ways.
    /// </summary>
    private static string NormalizeNamespace(string? @namespace)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            return DefaultNamespace;
        }

        var trimmed = @namespace!.Trim();

        // The comma is what ends the namespace in the header, so one inside it would
        // truncate the name and leave the remainder to be parsed as the payload.
        if (trimmed.Contains(','))
        {
            throw new ArgumentException("A namespace cannot contain a comma; it is the header's separator.",
                nameof(@namespace));
        }

        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    /// <summary>
    ///     Whether an event name is one the protocol keeps for itself.
    /// </summary>
    /// <remarks>
    ///     A server rejects a packet whose first argument is one of these, and a
    ///     rejected packet costs the whole connection rather than just the message.
    /// </remarks>
    private static bool IsReservedEventName(string @event)
    {
        return @event is "connect" or "connect_error" or "disconnect" or "disconnecting"
            or "newListener" or "removeListener";
    }

    private static bool CarriesEventName(PacketType type)
    {
        return type is PacketType.Event or PacketType.BinaryEvent;
    }

    private static bool CarriesAckId(PacketType type)
    {
        return type is PacketType.Event or PacketType.Ack or PacketType.BinaryEvent or PacketType.BinaryAck;
    }
}