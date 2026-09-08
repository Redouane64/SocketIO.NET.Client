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
    void Should_Create_Event_Packet()
    {
        var packet = new Packet(PacketType.Event);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal("message", packet.Event);
    }

    [Fact]
    void Should_Create_Event_Packet_With_Namespace()
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
    void Should_Normalize_Namespace(string @namespace)
    {
        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem("Hello!");

        Assert.Equal("/test", packet.Namespace);
        Assert.Equal("""2/test,["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Create_Event_Packet_With_Event_Name()
    {
        var eventName = "test";

        var packet = new Packet(PacketType.Event, null, eventName);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal(eventName, packet.Event);
    }

    [Fact]
    void Should_Create_Ack_Packet_With_Ack_Id()
    {
        var ackId = 42;

        var packet = new Packet(PacketType.Ack, ackId, null, null);

        Assert.Equal(PacketType.Ack, packet.Type);
        Assert.Equal(ackId, packet.AckId);
    }

    [Fact(DisplayName = "An event can request an acknowledgement")]
    void Should_Request_An_Acknowledgement_On_An_Event()
    {
        var ackId = 7;

        var packet = new Packet(PacketType.Event, ackId, null, null);
        packet.AddItem("Hello!");

        Assert.Equal(ackId, packet.AckId);
        Assert.Equal("""27["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "An acknowledgement does not carry an event name")]
    void Should_Reject_An_Event_Name_On_An_Acknowledgement()
    {
        Assert.Throws<ArgumentException>(() => new Packet(PacketType.Ack, null, "test"));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_Packet()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem("Hello!");

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", encodedPacket);
    }

    [Fact]
    void Should_Reject_Binary_On_A_Plaintext_Event()
    {
        var packet = new Packet(PacketType.Event);
        var invalidPayload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidOperationException>(() => packet.AddItem(invalidPayload));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_With_Namespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $"""2/{@namespace},["message","Hello!"]""";

        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_With_Event_Name()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}","Hello!"]""";

        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Plaintext_Ack_Packet()
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
    void Should_Serialize_Json_Event_Packet()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem(new Foo { Value = "bar" });

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message",{"Value":"bar"}]""", encodedPacket);
    }

    [Fact]
    void Should_Serialize_Json_Event_With_Namespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""2/{{@namespace}},["message",{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Event, @namespace);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Json_Event_With_Event_Name()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}",{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Event, null, eventName);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Json_Ack_Packet()
    {
        var ackId = 42;
        var expectedEncodedPacket = $$"""3{{ackId}}[{"Value":"bar"}]""";

        var packet = new Packet(PacketType.Ack, ackId, null, null);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "The same packet encodes identically every time it is sent")]
    void Should_Serialize_The_Same_Packet_Repeatedly()
    {
        var packet = new Packet(PacketType.Event);
        packet.AddItem("Hello!");

        var first = Encoding.UTF8.GetString(packet.Serialize().Span);
        var second = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", first);
        Assert.Equal(first, second);
    }
}