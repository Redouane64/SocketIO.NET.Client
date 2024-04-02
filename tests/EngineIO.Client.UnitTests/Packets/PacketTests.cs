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

    [Theory(DisplayName = "Parse plain-text packets")]
    [InlineData(PacketType.Open, new[] { (byte)PacketType.Open })]
    [InlineData(PacketType.Close, new[] { (byte)PacketType.Close })]
    [InlineData(PacketType.Ping, new[] { (byte)PacketType.Ping })]
    [InlineData(PacketType.Pong, new[] { (byte)PacketType.Pong })]
    [InlineData(PacketType.Message, new[] { (byte)PacketType.Message, (byte)'h', (byte)'i' })]
    [InlineData(PacketType.Upgrade, new[] { (byte)PacketType.Upgrade })]
    void Parse_Should_Parse_Packet(PacketType expectedType, ReadOnlyMemory<byte> message)
    {
        var success = Packet.TryParse(message, out var packet);
        Assert.True(success);
        Assert.Equal(expectedType, packet.Type);
    }

    [Fact]
    void Parse_Should_Throw_Invalid_Packet_Type()
    {
        var success = Packet.TryParse(new byte[] { 1, 2, 3 }, out var packet);
        Assert.False(success);
        Assert.Equal(default(Packet), packet);
    }

    [Fact]
    void PingProbePacket_Should_Be_Valid()
    {
        var packet = Packet.PingProbePacket;
        Assert.Equal((byte)PacketType.Ping, packet.ToPlaintextPacket().Span[0]);
    }
}
