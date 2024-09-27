using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocketIO.Client.Packets;

internal interface PacketData
{
    void Serialize(Utf8JsonWriter stream);

}
internal sealed class TextPacketData : PacketData
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

internal sealed class JsonPacketData<T> : PacketData where T : class
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

internal sealed class BinaryPacketData : PacketData
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
    public int Id { get; set; }

    public ReadOnlySpan<byte> Serialize()
    {
        throw new NotImplementedException();
    }

    public void Serialize(Utf8JsonWriter stream)
    {
        JsonSerializer.Serialize(stream, this);
    }
}
