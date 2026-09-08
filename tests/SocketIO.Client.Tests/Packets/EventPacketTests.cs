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
        var packet = new PacketBuilder(PacketType.Event);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal("message", packet.Event);
    }

    [Fact]
    void Should_Create_Event_Packet_With_Namespace()
    {
        var @namespace = "test";

        var packet = new PacketBuilder(PacketType.Event, @namespace);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal($"/{@namespace}", packet.Namespace);
        Assert.Equal("message", packet.Event);
    }

    [Theory(DisplayName = "A namespace encodes the same however it was spelled")]
    [InlineData("test")]
    [InlineData("/test")]
    void Should_Normalize_Namespace(string @namespace)
    {
        var packet = new PacketBuilder(PacketType.Event, @namespace);
        packet.AddItem("Hello!");

        Assert.Equal("/test", packet.Namespace);
        Assert.Equal("""2/test,["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "A comma would end the namespace early, so one is refused")]
    void Should_Reject_A_Namespace_Containing_A_Comma()
    {
        Assert.Throws<ArgumentException>(() => new PacketBuilder(PacketType.Event, "a,b"));
    }

    [Fact]
    void Should_Create_Event_Packet_With_Event_Name()
    {
        var eventName = "test";

        var packet = new PacketBuilder(PacketType.Event, null, eventName);

        Assert.Equal(PacketType.Event, packet.Type);
        Assert.Equal("/", packet.Namespace);
        Assert.Equal(eventName, packet.Event);
    }

    [Theory(DisplayName = "An event name the protocol keeps for itself is refused")]
    [InlineData("connect")]
    [InlineData("connect_error")]
    [InlineData("disconnect")]
    [InlineData("disconnecting")]
    [InlineData("newListener")]
    [InlineData("removeListener")]
    void Should_Reject_A_Reserved_Event_Name(string eventName)
    {
        Assert.Throws<ArgumentException>(() => new PacketBuilder(PacketType.Event, null, eventName));
    }

    [Fact]
    void Should_Create_Ack_Packet_With_Ack_Id()
    {
        var ackId = 42;

        var packet = new PacketBuilder(PacketType.Ack, ackId, null, null);

        Assert.Equal(PacketType.Ack, packet.Type);
        Assert.Equal(ackId, packet.AckId);
    }

    [Fact(DisplayName = "An event can request an acknowledgement")]
    void Should_Request_An_Acknowledgement_On_An_Event()
    {
        var ackId = 7;

        var packet = new PacketBuilder(PacketType.Event, ackId, null, null);
        packet.AddItem("Hello!");

        Assert.Equal(ackId, packet.AckId);
        Assert.Equal("""27["message","Hello!"]""", Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "An acknowledgement does not carry an event name")]
    void Should_Reject_An_Event_Name_On_An_Acknowledgement()
    {
        Assert.Throws<ArgumentException>(() => new PacketBuilder(PacketType.Ack, 1, null, "test"));
    }

    [Theory(DisplayName = "An acknowledgement has to name the event it answers")]
    [InlineData(PacketType.Ack)]
    [InlineData(PacketType.BinaryAck)]
    void Should_Reject_An_Acknowledgement_Without_An_Ack_Id(PacketType type)
    {
        Assert.Throws<ArgumentException>(() => new PacketBuilder(type, null, null));
    }

    [Theory(DisplayName = "A sign would be read as the start of the payload")]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    void Should_Reject_A_Negative_Ack_Id(int ackId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketBuilder(PacketType.Event, ackId, null, null));
    }

    [Theory(DisplayName = "Only the types that take part in one carry an ack id")]
    [InlineData(PacketType.Connect)]
    [InlineData(PacketType.Disconnect)]
    [InlineData(PacketType.ConnectError)]
    void Should_Reject_An_Ack_Id_On_A_Type_That_Cannot_Carry_One(PacketType type)
    {
        Assert.Throws<ArgumentException>(() => new PacketBuilder(type, 1, null, null));
    }

    [Fact]
    void Should_Reject_An_Unknown_Packet_Type()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketBuilder((PacketType)0x39));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_Packet()
    {
        var packet = new PacketBuilder(PacketType.Event);
        packet.AddItem("Hello!");

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", encodedPacket);
    }

    [Fact]
    void Should_Reject_Binary_On_A_Plaintext_Event()
    {
        var packet = new PacketBuilder(PacketType.Event);
        var invalidPayload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidOperationException>(() => packet.AddItem(invalidPayload));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_With_Namespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $"""2/{@namespace},["message","Hello!"]""";

        var packet = new PacketBuilder(PacketType.Event, @namespace);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Plaintext_Event_With_Event_Name()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}","Hello!"]""";

        var packet = new PacketBuilder(PacketType.Event, null, eventName);
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

        var packet = new PacketBuilder(PacketType.Ack, ackId, null, null);
        packet.AddItem("Hello!");

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Json_Event_Packet()
    {
        var packet = new PacketBuilder(PacketType.Event);
        packet.AddItem(new Foo { Value = "bar" });

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message",{"Value":"bar"}]""", encodedPacket);
    }

    [Fact]
    void Should_Serialize_Json_Event_With_Namespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""2/{{@namespace}},["message",{"Value":"bar"}]""";

        var packet = new PacketBuilder(PacketType.Event, @namespace);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Json_Event_With_Event_Name()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""2["{{eventName}}",{"Value":"bar"}]""";

        var packet = new PacketBuilder(PacketType.Event, null, eventName);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Json_Ack_Packet()
    {
        var ackId = 42;
        var expectedEncodedPacket = $$"""3{{ackId}}[{"Value":"bar"}]""";

        var packet = new PacketBuilder(PacketType.Ack, ackId, null, null);
        packet.AddItem(new Foo { Value = "bar" });

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "The same packet encodes identically every time it is sent")]
    void Should_Serialize_The_Same_Packet_Repeatedly()
    {
        var packet = new PacketBuilder(PacketType.Event);
        packet.AddItem("Hello!");

        var first = Encoding.UTF8.GetString(packet.Serialize().Span);
        var second = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""2["message","Hello!"]""", first);
        Assert.Equal(first, second);
    }
}