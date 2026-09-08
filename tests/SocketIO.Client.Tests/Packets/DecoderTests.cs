using SocketIO.Client.Exceptions;
using SocketIO.Client.Packets;

using EnginePacket = EngineIO.Client.Packets.Packet;

namespace SocketIO.Client.Tests.Packets;

public class DecoderTests
{
    [Fact]
    void Should_Not_Be_Reconstructing_Before_Anything_Arrives()
    {
        var decoder = new Decoder();

        Assert.False(decoder.IsReconstructing);
    }

    [Fact(DisplayName = "An attachment with no header before it means the stream is out of step")]
    void Should_Reject_An_Attachment_With_No_Packet_Waiting()
    {
        var decoder = new Decoder();

        Assert.Throws<PacketFormatException>(
            () => decoder.Add(EnginePacket.CreateBinaryPacket(new byte[] { 1, 2, 3 })));
    }
}