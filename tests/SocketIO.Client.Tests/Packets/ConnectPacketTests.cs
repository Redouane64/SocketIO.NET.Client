using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class ConnectPacketTests
{
    [Fact]
    void ShouldCreateConnectPacket()
    {
        var packet = Packet.ConnectPacket;
        Assert.Equal(PacketType.Connect, packet.Type);
        Assert.Equal("/", packet.Namespace);
    }
    
    [Fact]
    void ShouldCreateConnectPacketWithNamespace()
    {
        var @namespace = "test";
        
        var packet = new Packet(PacketType.Connect, @namespace);
        
        Assert.Equal(PacketType.Connect, packet.Type);
        Assert.Equal(@namespace, packet.Namespace);
    }
    
    [Fact]
    void ShouldSerializeConnectPacket()
    {
        var connectPacket = Packet.ConnectPacket;

        var serialized = connectPacket.Serialize();
        var plaintext = Encoding.UTF8.GetString(serialized.ToArray());
        
        Assert.Equal(PacketType.Connect, connectPacket.Type);
        Assert.Equal("0", plaintext);
    }
    
    [Fact]
    void ShouldSerializeConnectPacketWithNamespace()
    {
        var @namespace = "test";
        var connectPacket = new Packet(PacketType.Connect, @namespace);;

        var serialized = connectPacket.Serialize();
        var plaintext = Encoding.UTF8.GetString(serialized.ToArray());
        
        Assert.Equal(PacketType.Connect, connectPacket.Type);
        Assert.Equal($"0/{@namespace},", plaintext);
    }
    
}