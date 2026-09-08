using System.Text;

using SocketIO.Client.Packets;

namespace SocketIO.Client.Tests.Packets;

public class BinaryEventPacketTests
{
    [Fact]
    void Should_Create_Binary_Event_Packet()
    {
        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        Assert.Equal(PacketType.BinaryEvent, packet.Type);
        Assert.Equal("/", packet.Namespace);
    }

    [Fact]
    void Should_Create_Binary_Packet_With_Namespace()
    {
        var @namespace = "test";

        var packet = new PacketBuilder(PacketType.BinaryEvent, @namespace);

        Assert.Equal($"/{@namespace}", packet.Namespace);
    }

    [Fact]
    void Should_Create_Binary_Packet_With_Event_Name()
    {
        var eventName = "test";

        var packet = new PacketBuilder(PacketType.BinaryEvent, null, eventName);

        Assert.Equal(eventName, packet.Event);
    }

    [Fact]
    void Should_Create_Binary_Packet_With_Ack_Id()
    {
        var ackId = 42;

        var packet = new PacketBuilder(PacketType.BinaryAck, ackId, null, null);

        Assert.Equal(ackId, packet.AckId);
    }

    [Fact]
    void Should_Serialize_Binary_Event_Packet()
    {
        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        var encodedPacket = Encoding.UTF8.GetString(packet.Serialize().Span);

        Assert.Equal("""51-["message",{"_placeholder":true,"num":0}]""", encodedPacket);
    }

    [Fact]
    void Should_Serialize_Binary_Packet_With_Namespace()
    {
        var @namespace = "test";
        var expectedEncodedPacket = $$"""51-/{{@namespace}},["message",{"_placeholder":true,"num":0}]""";

        var packet = new PacketBuilder(PacketType.BinaryEvent, @namespace);
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Binary_Packet_With_Event_Name()
    {
        var eventName = "test";
        var expectedEncodedPacket = $$"""51-["{{eventName}}",{"_placeholder":true,"num":0}]""";

        var packet = new PacketBuilder(PacketType.BinaryEvent, null, eventName);
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact]
    void Should_Serialize_Binary_Packet_With_Ack_Id()
    {
        var ackId = 42;

        // An acknowledgement answers an event rather than naming one.
        var expectedEncodedPacket = $$"""61-{{ackId}}[{"_placeholder":true,"num":0}]""";

        var packet = new PacketBuilder(PacketType.BinaryAck, ackId, null, null);
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Theory(DisplayName = "A decoder refuses a binary packet that announces nothing to attach")]
    [InlineData(PacketType.BinaryEvent)]
    [InlineData(PacketType.BinaryAck)]
    void Should_Reject_A_Binary_Packet_Without_An_Attachment(PacketType type)
    {
        var packet = type == PacketType.BinaryAck
            ? new PacketBuilder(type, 1, null, null)
            : new PacketBuilder(type);
        packet.AddItem("Hello!");

        Assert.Throws<InvalidOperationException>(() => packet.Serialize());
    }

    [Fact(DisplayName = "Attachments are numbered in the order they were added")]
    void Should_Number_Attachments_In_Order()
    {
        var first = new ReadOnlyMemory<byte>([1, 2, 3]);
        var second = new ReadOnlyMemory<byte>([4, 5, 6]);
        var expectedEncodedPacket =
            """52-["message",{"_placeholder":true,"num":0},{"_placeholder":true,"num":1}]""";

        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem(first);
        packet.AddItem(second);

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
        Assert.Equal([first, second], packet.Attachments);
    }

    [Fact(DisplayName = "Binary and text arguments can be mixed in one payload")]
    void Should_Mix_Text_And_Binary_Arguments()
    {
        var expectedEncodedPacket = """51-["message","Hello!",{"_placeholder":true,"num":0}]""";

        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem("Hello!");
        packet.AddItem(new ReadOnlyMemory<byte>([1, 2, 3]));

        Assert.Equal(expectedEncodedPacket, Encoding.UTF8.GetString(packet.Serialize().Span));
    }

    [Fact(DisplayName = "A byte array is treated as binary, not as a Json value")]
    void Should_Treat_A_Byte_Array_As_Binary()
    {
        byte[] attachment = [1, 2, 3];

        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem(attachment);

        Assert.Equal("""51-["message",{"_placeholder":true,"num":0}]""",
            Encoding.UTF8.GetString(packet.Serialize().Span));
        Assert.Equal(attachment, Assert.Single(packet.Attachments));
    }

    [Fact(DisplayName = "The attachments are kept out of the encoded header")]
    void Should_Keep_Attachments_Out_Of_The_Header()
    {
        var attachment = new ReadOnlyMemory<byte>([1, 2, 3]);

        var packet = new PacketBuilder(PacketType.BinaryEvent);
        packet.AddItem(attachment);

        Assert.Equal(attachment, Assert.Single(packet.Attachments));
        Assert.DoesNotContain((byte)0x01, packet.Serialize().Span.ToArray());
    }
}