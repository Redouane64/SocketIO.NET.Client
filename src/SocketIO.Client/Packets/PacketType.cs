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
    public static readonly Packet ConnectPacket = new(PacketType.Connect);

    public static Packet DisconnectPacket = new(PacketType.Disconnect);

    const string DefaultNamespace = "/";
    
    const string DefaultEventName = "message";

    public PacketType Type { get; }
    public string? Namespace { get; }
    public uint? AckId { get; }
    public string Event { get; }
    
    private readonly List<IPacketData> _data = new();
    
    private readonly MemoryStream _buffer = new();

    public Packet(PacketType type)
        : this(type, DefaultNamespace, DefaultEventName)
    {
        Type = type;
    }
    
    public Packet(PacketType type, string @namespace)
        : this(type, @namespace, DefaultEventName)
    {
        Type = type;
        Namespace = @namespace;
    }
    
    public Packet(PacketType type, string? @namespace, string @event)
    {
        if ((type == PacketType.Connect || type == PacketType.Disconnect) && @event != DefaultEventName)
        {
            throw new ArgumentException();
        }
        
        Type = type;
        Namespace = @namespace ?? DefaultNamespace;
        Event = @event!;
        
        if (type is PacketType.Event or PacketType.BinaryEvent)
        {
            this.AddItem(Event);
        }
    }
    
    public Packet(PacketType type, string @namespace, string @event, uint ackId)
        : this(type, @namespace, @event)
    {
        if (type != PacketType.Ack && type != PacketType.BinaryAck)
        {
            throw new ArgumentException();
        }
        
        AckId = ackId;
    }

    private void _AddItem<T>(T data) where T : IPacketData
    {
        if (Type == PacketType.Connect || Type == PacketType.Disconnect)
        {
            throw new InvalidOperationException();
        }

        this._data.Add(data);
    }

    /// <summary>
    /// Add plain text data to packet
    /// </summary>
    /// <param name="data">Plain text data</param>
    public void AddItem(string data)
    {
        if (Type == PacketType.BinaryEvent || Type == PacketType.BinaryAck)
        {
            throw new InvalidOperationException();
        }
        
        this._AddItem(new TextPacketData(data));
    }
    
    /// <summary>
    /// Add binary data
    /// </summary>
    /// <param name="data">Binary data</param>
    public void AddItem(ReadOnlyMemory<byte> data)
    {
        if (Type == PacketType.Event || Type == PacketType.Ack)
        {
            throw new InvalidOperationException();
        }
        
        int id = this._data.OfType<BinaryPacketData>().Count();
        this._AddItem(new BinaryPacketData(id, data));
    }
    
    /// <summary>
    /// Add Json serializable POCO
    /// </summary>
    /// <param name="data">Data instance</param>
    /// <typeparam name="T">Data type</typeparam>
    public void AddItem<T>(T data) where T : class
    {
        if (Type == PacketType.BinaryEvent || Type == PacketType.BinaryAck)
        {
            throw new InvalidOperationException();
        }
        
        this._AddItem(new JsonPacketData<T>(data));
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
        if (Type is PacketType.BinaryEvent || Type == PacketType.BinaryAck)
        {
            var count = this._data.OfType<BinaryPacketData>().Count();
            if (count > 0)
            {
                headerWriter.Write(count);
                headerWriter.Write('-');
            }
        }

        // write packet namespace
        if (Namespace != DefaultNamespace)
        {
            headerWriter.Write('/');
            headerWriter.Write(this.Namespace);
            headerWriter.Write(',');
        }

        // write acknowledgement id if set
        if (this.AckId.HasValue)
        {
            headerWriter.Write(this.AckId);
        }
        headerWriter.Flush();
            
        // write packet content
        if (Type == PacketType.Connect || Type == PacketType.Disconnect)
        {
            return this._buffer;
        }
        
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