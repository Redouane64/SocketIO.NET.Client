using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SocketIO.Client.Packets;

public enum PacketType : byte
{
    /// <summary>
    /// Connect packet type
    /// </summary>
    Connect = 0x30,
    
    /// <summary>
    /// Disconnect packet type
    /// </summary>
    Disconnect = 0x31,
    
    /// <summary>
    /// Event packet type with Plaintext/JSON data
    /// </summary>
    Event = 0x32,
    
    /// <summary>
    /// Acknowledgement packet type with plaintext/json data
    /// </summary>
    Ack = 0x33,
    
    /// <summary>
    /// Connection error packet type
    /// </summary>
    ConnectError = 0x34,
    
    /// <summary>
    /// Event packet type with binary data
    /// </summary>
    BinaryEvent = 0x35,
    
    /// <summary>
    /// Acknowledgement packet type with binary data
    /// </summary>
    BinaryAck = 0x36
}

/**
 * SocketIO packet format:
 *
 * Initial Packet:
 * <packet type>[<# of binary attachments>-][<namespace>,][<acknowledgment id>][JSON-stringified payload without binary]
 *
 * Next Packets containing binary data
 *  + binary attachments extracted
 * 
 */

public class Packet
{
    public static Packet ConnectPacket = new(PacketType.Connect);

    public static Packet DisconnectPacket = new(PacketType.Disconnect);

    public PacketType Type { get; }
    public string? Namespace { get; internal set; }
    public uint? AckId { get; }

    public string Event { get; }
    
    private readonly List<IPacketData> _data = new();
    
    private readonly MemoryStream _buffer = new();

    public Packet(PacketType type)
        : this(type, "/", "message")
    {
        Type = type;
        Namespace = "/";
    }
    
    public Packet(PacketType type, string @namespace, string @event)
    {
        Type = type;
        Namespace = @namespace;
        Event = @event;
        this.AddItem(Event);
    }
    
    public Packet(PacketType type, string @namespace, string @event, uint ackId)
        : this(type, @namespace, @event)
    {
        AckId = ackId;
    }

    /// <summary>
    /// Add plain text data to packet
    /// </summary>
    /// <param name="data">Plain text data</param>
    public void AddItem(string data)
    {
        // TODO: check if packet type is not binary event, if it is, we should throw
        this._data.Add(new TextPacketData(data));
    }
    
    /// <summary>
    /// Add binary data
    /// </summary>
    /// <param name="data">Binary data</param>
    public void AddItem(ReadOnlyMemory<byte> data)
    {
        // TODO: check if packet type is binary event. if not, we should throw
        
        int id = this._data.OfType<BinaryPacketData>().Count();
        this._data.Add(new BinaryPacketData(id, data));
    }
    
    /// <summary>
    /// Add Json serializable POCO
    /// </summary>
    /// <param name="data">Data instance</param>
    /// <typeparam name="T">Data type</typeparam>
    public void AddItem<T>(T data) where T : class
    {
        this._data.Add(new JsonPacketData<T>(data));
    }

    /// <summary>
    /// Serialize the packet and return the underlying memory stream which contains the raw packet bytes.
    /// </summary>
    /// <returns>The underlying `MemoryStream`</returns>
    internal MemoryStream Serialize()
    {
        // write packet header
        using var headerWriter = new StreamWriter(this._buffer);
        using var dataWriter = new Utf8JsonWriter(this._buffer);

        // write packet type
        headerWriter.Write((char)this.Type);
        if (Type is PacketType.BinaryEvent or PacketType.BinaryAck && this._data.Count > 0)
        {
            var count = this._data.OfType<BinaryPacketData>().Count();
            headerWriter.Write(count);
            headerWriter.Write('-');
        }

        // write packet namespace
        headerWriter.Write(this.Namespace);
        headerWriter.Write(',');

        // write acknowledgement id if set
        if (this.AckId.HasValue)
        {
            headerWriter.Write(this.AckId);
        }
        headerWriter.Flush();
            
        // write packet content
        dataWriter.WriteStartArray();
        foreach (IPacketData item in this._data)
        {
            item.Serialize(dataWriter);
        }
        dataWriter.WriteEndArray();
        dataWriter.Flush();
        
        return this._buffer;
    }
}