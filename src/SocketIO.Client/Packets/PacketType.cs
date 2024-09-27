using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SocketIO.Client.Packets;

public enum PacketType : byte
{
    Connect = 0x30,
    Disconnect = 0x31,
    Event = 0x32,
    Ack = 0x33,
    ConnectError = 0x34,
    BinaryEvent = 0x35,
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
    
    private List<PacketData> _data = new();
    
    public Packet(PacketType type)
    {
        Type = type;
        Namespace = "/";
    }
    
    public Packet(PacketType type, string @namespace)
        : this(type)
    {
        Namespace = @namespace;
    }
    
    public Packet(PacketType type, string @namespace, uint ackId)
        : this(type, @namespace)
    {
        AckId = ackId;
    }

    /// <summary>
    /// Add plain text data to packet
    /// </summary>
    /// <param name="data">Plain text data</param>
    public void AddItem(string data)
    {
        this._data.Add(new TextPacketData(data));
    }
    
    /// <summary>
    /// Add binary data
    /// </summary>
    /// <param name="data">Binary data</param>
    public void AddItem(ReadOnlyMemory<byte> data)
    {
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

    internal ReadOnlySpan<byte> Encode()
    {
        using (var stream = new MemoryStream())
        {
            // Write packet header
            var headerWriter = new StreamWriter(stream);
            
            headerWriter.Write((char)this.Type);
            if (Type is PacketType.BinaryEvent or PacketType.BinaryAck && this._data.Count > 0)
            {
                var count = this._data.OfType<BinaryPacketData>().Count();
                headerWriter.Write(count);
                headerWriter.Write('-');
            }

            // Write packet namespace
            headerWriter.Write(this.Namespace);
            headerWriter.Write(',');

            // Write acknowledgement id if set
            if (this.AckId.HasValue)
            {
                headerWriter.Write(this.AckId);
            }
            headerWriter.Flush();
            
            // Write packet content
            var dataWriter = new Utf8JsonWriter(stream);
            dataWriter.WriteStartArray();
            foreach (PacketData item in this._data)
            {
                if (item is TextPacketData plainText)
                {
                    plainText.Serialize(dataWriter);
                }
                else if (item is BinaryPacketData binary)
                {
                    binary.Serialize(dataWriter);
                }
                else
                {
                    item.Serialize(dataWriter);
                }
            }
            dataWriter.WriteEndArray();
            dataWriter.Flush();
            
            stream.Seek(0, SeekOrigin.Begin);
            var data = stream.ToArray();

            headerWriter.Dispose();
            
            return data;
        }
    }
}