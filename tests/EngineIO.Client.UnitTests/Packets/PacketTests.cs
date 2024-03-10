using System.Text;
using EngineIO.Client.Packets;

namespace EngineIO.Client.UnitTests.Packets;

public class PacketTests
{
    [Fact]
    void Create_Payload_From_Plaintext_Message()
    {
        var packet = new Packet(
            PacketFormat.PlainText,
            PacketType.Message,
            new[] { (byte)'H', (byte)'i' })
            .ToPayload();
        
        Assert.Equal((byte)PacketType.Message, packet.Span[0]);
        Assert.Equal((byte)'H', packet.Span[1]);
        Assert.Equal((byte)'i', packet.Span[2]);
    }
    
    [Fact]
    void Create_Payload_From_Binary_Message()
    {
        var body = Encoding.UTF8.GetBytes("Hi");
        var base64 = Convert.ToBase64String(body);
        var packet = new Packet(
                PacketFormat.Binary,
                PacketType.Message,
                new[] { (byte)'H', (byte)'i' })
            .ToPayload();
        
        Assert.Equal((byte)'b', packet.Span[0]);
        Assert.Equal((byte)'H', packet.Span[1]);
        Assert.Equal((byte)'i', packet.Span[2]);
    }
}
