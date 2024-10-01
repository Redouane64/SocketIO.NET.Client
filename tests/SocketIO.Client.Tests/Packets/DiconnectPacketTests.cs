using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class DisconnectPacketTests
{
    [Fact]
    void ShouldCreateDisconnectPacket()
    {
        var packet = Packet.DisconnectPacket;
        
        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal("/", packet.Namespace);
    }
    
    [Fact]
    void ShouldCreateDisconnectPacketWithNamespace()
    {
        var @namespace = "test";
        var packet = new Packet(PacketType.Disconnect, @namespace);
        
        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal(@namespace, packet.Namespace);
    }
    
    [Fact]
    void ShouldSerializeDisconnectPacket()
    {
        var connectPacket = Packet.DisconnectPacket;

        var serialized = connectPacket.Serialize();
        var encodedPacket = Encoding.UTF8.GetString(serialized.ToArray());
        
        string expectedEncodedPacket = "1";
        
        Assert.Equal(PacketType.Disconnect, connectPacket.Type);
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
    
    [Fact]
    void ShouldSerializeDisconnectPacketWithNamespace()
    {
        var @namespace = "test";
        var packet = new Packet(PacketType.Disconnect, @namespace);;

        var serialized = packet.Serialize();
        var encodedPacket = Encoding.UTF8.GetString(serialized.ToArray());
        
        string expectedEncodedPacket = $"1/{@namespace},";
        
        Assert.Equal(PacketType.Disconnect, packet.Type);
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
}