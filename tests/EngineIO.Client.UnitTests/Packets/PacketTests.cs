using System.Text;
using EngineIO.Client.Packets;
using EngineIO.Client.Transport;

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
            .ToPlaintextPacket();

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
            .ToBinaryPacket(new Base64Encoder());

        Assert.Equal((byte)'b', packet.Span[0]);
        Assert.Equal((byte)base64[0], packet.Span[1]);
        Assert.Equal((byte)base64[1], packet.Span[2]);
        Assert.Equal((byte)base64[2], packet.Span[3]);
        Assert.Equal((byte)base64[3], packet.Span[4]);
    }
}
