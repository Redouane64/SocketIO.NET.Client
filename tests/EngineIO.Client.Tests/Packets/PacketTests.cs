using System.Text;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports;

namespace EngineIO.Client.Tests.Packets;

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
    void Parse_Should_Parse_Packet(PacketType expectedType, byte[] message)
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

    [Fact]
    void Parse_Should_Reject_Empty_Payload()
    {
        var success = Packet.TryParse(ReadOnlyMemory<byte>.Empty, out var packet);

        Assert.False(success);
        Assert.Equal(default, packet);
    }

    [Fact]
    void Parse_Should_Reject_Base64_Prefixed_Payload()
    {
        // 'b' marks base64 binary, which is long-polling framing decoded by the
        // transport. The shared parser only understands plain-text packets.
        var success = Packet.TryParse(Encoding.UTF8.GetBytes("bSGk="), out _);

        Assert.False(success);
    }

    [Fact]
    void Create_Payload_Should_Preserve_Body_Larger_Than_One_Buffer()
    {
        var body = new byte[10_000];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 251);
        }

        var payload = new Packet(PacketFormat.PlainText, PacketType.Message, body).ToPlaintextPacket();

        Assert.Equal(body.Length + 1, payload.Length);
        Assert.Equal((byte)PacketType.Message, payload.Span[0]);
        Assert.True(payload.Span[1..].SequenceEqual(body));
    }

    [Fact]
    void Packet_Length_Should_Include_Type_Byte()
    {
        var packet = Packet.CreateMessagePacket("Hello");

        Assert.Equal(5, packet.Body.Length);
        Assert.Equal(6, packet.Length);
    }
}