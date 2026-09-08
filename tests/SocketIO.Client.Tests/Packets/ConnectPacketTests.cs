using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class ConnectPacketTests
{
    [Fact]
    void Should_Create_Connect_Packet()
    {
        var packet = PacketBuilder.Connect;

        Assert.Equal(PacketType.Connect, packet.Type);
        Assert.Equal("/", packet.Namespace);
    }

    [Fact]
    void Should_Create_Connect_Packet_With_Namespace()
    {
        var @namespace = "test";

        var packet = new PacketBuilder(PacketType.Connect, @namespace);

        Assert.Equal(PacketType.Connect, packet.Type);
        Assert.Equal($"/{@namespace}", packet.Namespace);
    }

    [Fact]
    void Should_Serialize_Connect_Packet()
    {
        var connectPacket = PacketBuilder.Connect;

        var encodedPacket = Encoding.UTF8.GetString(connectPacket.Serialize().Span);

        Assert.Equal(PacketType.Connect, connectPacket.Type);
        Assert.Equal("0", encodedPacket);
    }

    [Fact]
    void Should_Serialize_Connect_Packet_With_Namespace()
    {
        var @namespace = "test";
        var connectPacket = new PacketBuilder(PacketType.Connect, @namespace);

        var encodedPacket = Encoding.UTF8.GetString(connectPacket.Serialize().Span);

        Assert.Equal(PacketType.Connect, connectPacket.Type);
        Assert.Equal($"0/{@namespace},", encodedPacket);
    }

    [Fact]
    void Should_Reject_A_Payload_On_A_Connect_Packet()
    {
        var packet = PacketBuilder.Connect;

        Assert.Throws<InvalidOperationException>(() => packet.AddItem("World"));
    }

    [Fact(DisplayName = "The shared Connect packet can be serialized more than once")]
    void Should_Serialize_The_Shared_Connect_Packet_Repeatedly()
    {
        var first = Encoding.UTF8.GetString(PacketBuilder.Connect.Serialize().Span);
        var second = Encoding.UTF8.GetString(PacketBuilder.Connect.Serialize().Span);

        Assert.Equal("0", first);
        Assert.Equal(first, second);
    }
}