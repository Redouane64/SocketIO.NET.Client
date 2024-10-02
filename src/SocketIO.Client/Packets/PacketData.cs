using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocketIO.Client.Packets;

internal interface IPacketData
{
    void Serialize(Utf8JsonWriter stream);
}

internal sealed class TextPacketData : IPacketData
{
    public string Data { get; }
    
    public TextPacketData(string data)
    {
        Data = data;
    }
    
    public void Serialize(Utf8JsonWriter stream)
    {
        stream.WriteStringValue(this.Data);
    }
}

internal sealed class JsonPacketData<T> : IPacketData where T : class
{
    public T Data { get; }

    public JsonPacketData(T data)
    {
        Data = data;
    }

    public void Serialize(Utf8JsonWriter stream)
    {
        JsonSerializer.Serialize(stream, Data);
    }
}

internal sealed class BinaryPacketData : IPacketData
{
    public BinaryPacketData(int id, ReadOnlyMemory<byte> data)
    {
        Id = id;
        Data = data;
    }

    [JsonIgnore]
    public ReadOnlyMemory<byte> Data { get; }
    
    [JsonPropertyName("_placeholder")] 
    public bool Placeholder => true;

    [JsonPropertyName("num")]
    public int Id { get; }

    public void Serialize(Utf8JsonWriter stream)
    {
        JsonSerializer.Serialize(stream, this);
    }
}
