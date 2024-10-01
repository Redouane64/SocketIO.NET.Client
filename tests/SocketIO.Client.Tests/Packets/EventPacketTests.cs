using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

// class used for packet with json payload tests
class Foo
{
    public string? Value { get; set; }
}


public class EventPacketTests
{
    [Fact]
    void ShouldCreateEventPacket()
    {
        var packet = new Packet(PacketType.Event);
        
        string expectedDefaultNamespace = "/";
        string expectedDefaultEvent = "message";
        
        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal(expectedDefaultNamespace, packet.Namespace);
        Assert.Equal(expectedDefaultEvent, packet.Event);
    }
    
    [Fact]
    void ShouldCreateEventPacketWithNamespace()
    {
        var @namespace = "test";
        var packet = new Packet(PacketType.Event, "test");
        
        var expectedNamespace = @namespace;
        var expectedDefaultEvent = "message";
        
        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal(expectedNamespace, packet.Namespace);
        Assert.Equal(expectedDefaultEvent, packet.Event);
    }
    
    [Fact]
    void ShouldCreateEventPacketWithEventName()
    {
        var eventName = "test";
        var packet = new Packet(PacketType.Event, null, eventName);

        var expectedDefaultNamespace = "/";
        var expectedDefaultEvent = eventName;
        
        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal(expectedDefaultNamespace, packet.Namespace);
        Assert.Equal(expectedDefaultEvent, packet.Event);
    }
    
    [Fact]
    void ShouldSerializePlainTextEventPacket()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem("Hello!");
        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        string expectedEncodedPacket = """2["message","Hello!"]""";
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }

    [Fact]
    void ShouldThrowWhenAddingInvalidPayloadPacket()
    {
        var packet = new Packet(PacketType.Event);
        var invalidPayload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidOperationException>(() => packet.AddItem(invalidPayload));
    }
    
    [Fact]
    void ShouldSerializePlainTextEventWithNamespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $"""2/{@namespace},["message","Hello!"]""";
        
        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem("Hello!");
        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
    
    [Fact]
    void ShouldSerializePlainTextEventWithEventName()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}","Hello!"]""";
        
        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem("Hello!");
        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
    
    [Fact]
    void ShouldSerializeJsonEventPacket()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem(new Foo { Value = "bar" });
        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        string expectedEncodedPacket = """2["message",{"Value":"bar"}]""";
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
    
    [Fact]
    void ShouldSerializeJsonEventPacketWithNamespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""2/{{@namespace}},["message",{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem(new Foo { Value = "bar" });
        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
    
    [Fact]
    void ShouldSerializeJsonEventWithEventName()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}",{"Value":"bar"}]""";
        
        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem(new Foo { Value = "bar" });

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().ToArray());
        
        Assert.Equal(expectedEncodedPacket, encodedPacket);
    }
}