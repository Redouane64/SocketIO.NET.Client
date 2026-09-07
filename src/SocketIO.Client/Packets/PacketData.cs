using System;
using System.Text.Json;

namespace SocketIO.Client.Packets;

/// <summary>
///     One argument of a packet payload, able to write itself into the payload array.
/// </summary>
/// <remarks>
///     The payload is a JSON array of mixed arguments, so each argument owns how it is
///     written rather than the packet switching over shapes it does not know about.
/// </remarks>
internal interface IPacketData
{
    void Serialize(Utf8JsonWriter writer);
}

/// <summary>
///     A plain text argument. Also carries the event name, which is nothing more than
///     the first argument of an event payload.
/// </summary>
internal sealed class TextPacketData : IPacketData
{
    public TextPacketData(string data)
    {
        Data = data;
    }

    public string Data { get; }

    public void Serialize(Utf8JsonWriter writer)
    {
        writer.WriteStringValue(Data);
    }
}

/// <summary>
///     An argument serialized from a POCO.
/// </summary>
internal sealed class JsonPacketData<T> : IPacketData where T : class
{
    public JsonPacketData(T data)
    {
        Data = data;
    }

    public T Data { get; }

    public void Serialize(Utf8JsonWriter writer)
    {
        // TODO: take a JsonTypeInfo<T> so callers can supply a source-generated
        // context, the way the Engine.io handshake already does.
        JsonSerializer.Serialize(writer, Data);
    }
}

/// <summary>
///     The placeholder a binary argument leaves behind in the payload.
/// </summary>
/// <remarks>
///     Binary never travels inside the JSON: the payload holds
///     <c>{"_placeholder":true,"num":N}</c> and the bytes follow the header as the
///     Nth separate binary packet.
/// </remarks>
internal sealed class BinaryPacketData : IPacketData
{
    public BinaryPacketData(int id, ReadOnlyMemory<byte> data)
    {
        Id = id;
        Data = data;
    }

    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    ///     Position of this attachment among the packet's binary arguments.
    /// </summary>
    public int Id { get; }

    public void Serialize(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("_placeholder", true);
        writer.WriteNumber("num", Id);
        writer.WriteEndObject();
    }
}