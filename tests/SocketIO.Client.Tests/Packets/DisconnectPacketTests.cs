using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class DisconnectPacketTests
{
    [Fact]
    void Should_Create_Disconnect_Packet()
    {
        var packet = PacketBuilder.Disconnect;

        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal("/", packet.Namespace);
    }

    [Fact]
    void Should_Create_Disconnect_Packet_With_Namespace()
    {
        var @namespace = "test";

        var packet = new PacketBuilder(PacketType.Disconnect, @namespace);

        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal($"/{@namespace}", packet.Namespace);
    }

    [Fact]
    void Should_Serialize_Disconnect_Packet()
    {
        var packet = PacketBuilder.Disconnect;

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal("1", encodedPacket);
    }

    [Fact]
    void Should_Serialize_Disconnect_Packet_With_Namespace()
    {
        var @namespace = "test";
        var packet = new PacketBuilder(PacketType.Disconnect, @namespace);

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal($"1/{@namespace},", encodedPacket);
    }

    [Fact]
    void Should_Reject_A_Payload_On_A_Disconnect_Packet()
    {
        var packet = PacketBuilder.Disconnect;

        Assert.Throws<InvalidOperationException>(() => packet.AddItem("World"));
    }
}