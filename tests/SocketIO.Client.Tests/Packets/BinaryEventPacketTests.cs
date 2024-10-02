using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class BinaryEventPacketTests
{
    [Fact]
    void ShouldCreateBinaryEventPacket()
    {
        var packetType = PacketType.BinaryEvent;
        var expectedPacketType = packetType;
        var expectedDefaultNamespace = "/";
        
        var packet = new Packet(packetType);
        packet.AddItem(new ReadOnlyMemory<byte>([ 1, 2, 3 ]));
        
        Assert.Equal(expectedPacketType, packet.Type);
        Assert.Equal(expectedDefaultNamespace, packet.Namespace);
    }

    [Fact]
    public void ShouldCreateBinaryPacketWithNamespace()
    {
        var @namespace = "test";
        var expectedNamespace = @namespace;
        
        var packet = new Packet(PacketType.BinaryEvent, @namespace);
        
        Assert.Equal(expectedNamespace, packet.Namespace);
    }

    [Fact]
    public void ShouldCreateBinaryPacketWithEventName()
    {
        var eventName = "test";
        var expectedEventName = eventName;
        
        var packet = new Packet(PacketType.BinaryEvent, null, eventName);
        
        Assert.Equal(expectedEventName, packet.Event);
    }

    [Fact]
    public void ShouldCreateBinaryPacketWithAckId()
    {
        var ackId = 42;
        var expectedAckId = ackId;
        
        var packet = new Packet(PacketType.BinaryAck, ackId, null, null);
        
        Assert.Equal(expectedAckId, packet.AckId);
    }

    [Fact]
    public void ShouldSerializeBinaryEventPacket()
    {
        var expectedEncodedPacket = $$"""51-["message",{"_placeholder":true,"num":0}]""";
        var packet = new Packet(PacketType.BinaryEvent);
        packet.AddItem(new ReadOnlyMemory<byte>([ 1, 2, 3 ]));
        
        var encodedPacket = Encoding.UTF8.GetString( packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }

    [Fact]
    public void ShouldSerializeBinaryPacketWithNamespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""51-/{{@namespace}},["message",{"_placeholder":true,"num":0}]""";
        
        var packet = new Packet(PacketType.BinaryEvent, @namespace);
        packet.AddItem(new ReadOnlyMemory<byte>([ 1, 2, 3 ]));
        
        var encodedPacket = Encoding.UTF8.GetString( packet.Serialize().ToArray());

        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }

    [Fact]
    public void ShouldSerializeBinaryPacketWithEventName()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""51-["{{eventName}}",{"_placeholder":true,"num":0}]""";
        
        var packet = new Packet(PacketType.BinaryEvent, null, eventName);
        packet.AddItem(new ReadOnlyMemory<byte>([ 1, 2, 3 ]));
        
        var encodedPacket = Encoding.UTF8.GetString( packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }

    [Fact]
    public void ShouldSerializeBinaryPacketWithAckId()
    {
        var ackId = 42;
        var expectedEncodedPacket = $$"""61-42["message",{"_placeholder":true,"num":0}]""";
        
        var packet = new Packet(PacketType.BinaryAck, ackId, null, null);
        packet.AddItem(new ReadOnlyMemory<byte>([ 1, 2, 3 ]));
        
        var encodedPacket = Encoding.UTF8.GetString( packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
}