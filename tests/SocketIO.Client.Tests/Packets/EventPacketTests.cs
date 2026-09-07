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

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal("message", packet.Event);
    }

    [Fact]
    void ShouldCreateEventPacketWithNamespace()
    {
        var @namespace = "test";

        var packet = new Packet(PacketType.Event, @namespace);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal($"/{@namespace}", packet.Namespace);
        Assert.Equal("message", packet.Event);
    }

    [Theory(DisplayName = "A namespace encodes the same however it was spelled")]
    [InlineData("test")]
    [InlineData("/test")]
    void ShouldNormalizeNamespace(string @namespace)
    {
        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem("Hello!");

        Assert.Equal("/test", packet.Namespace);
        Assert.Equal("""2/test,["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldCreateEventPacketWithEventName()
    {
        var eventName = "test";

        var packet = new Packet(PacketType.Event, null, eventName);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal(eventName, packet.Event);
    }

    [Fact]
    void ShouldCreateEventPacketWithAckId()
    {
        var ackId = 42;

        var packet = new Packet(PacketType.Ack, ackId, null, null);

        Assert.Equal(PacketType.Ack, packet.Type);
        Assert.Equal(ackId, packet.AckId);
    }

    [Fact(DisplayName = "An event can request an acknowledgement")]
    void ShouldCreateEventPacketRequestingAnAcknowledgement()
    {
        var ackId = 7;

        var packet = new Packet(PacketType.Event, ackId, null, null);
        packet.AddItem("Hello!");

        Assert.Equal(ackId, packet.AckId);
        Assert.Equal("""27["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "An acknowledgement does not carry an event name")]
    void ShouldRejectAnEventNameOnAnAcknowledgement()
    {
        Assert.Throws<ArgumentException>(() => new Packet(PacketType.Ack, null, "test"));
    }

    [Fact]
    void ShouldSerializePlainTextEventPacket()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem("Hello!");

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", encodedPacket);
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

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldSerializePlainTextEventWithEventName()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}","Hello!"]""";

        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldSerializePlainTextWithAckIdPacket()
    {
        var ackId = 42;

        // An acknowledgement answers an event rather than naming one, so its payload
        // is the response arguments alone.
        var expectedEncodedPacket = $$"""3{{ackId}}["Hello!"]""";

        var packet = new Packet(PacketType.Ack, ackId, null, null);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldSerializeJsonEventPacket()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem(new Foo { Value = "bar" });

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message",{"Value":"bar"}]""", encodedPacket);
    }

    [Fact]
    void ShouldSerializeJsonEventPacketWithNamespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""2/{{@namespace}},["message",{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldSerializeJsonEventWithEventName()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}",{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void ShouldSerializeJsonEventWithAckIdPacket()
    {
        var ackId = 42;
        var expectedEncodedPacket = $$"""3{{ackId}}[{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Ack, ackId, null, null);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "The same packet encodes identically every time it is sent")]
    void ShouldSerializeTheSamePacketRepeatedly()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem("Hello!");

        var first = Encoding.UTF8.GetString(packet.Serialize().Span);
        var second = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", first);
        Assert.Equal(first, second);
    }
}