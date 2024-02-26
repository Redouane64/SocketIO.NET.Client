namespace EngineIO.Client.UnitTests;

public class PacketParserTests
{
    [Fact]
    public void ShouldParsePayloadWithMultiPackets()
    {
        var parser = new DefaultPacketParser();
        var binary = new byte[]
        {
            0x34, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21, 0x21, 0x21, 0x1E, 0x32,
            0x1E, 0x34, 0x48, 0x65, 0x6C, 0x6C, 0x6F,
            0x21, 0x21, 0x21
        };
        var packets = parser.Parse(binary);

        Assert.Equal(3, packets.Count);
    }

    [Fact]
    public void ShouldParsePayloadWithSinglePacket()
    {
        var parser = new DefaultPacketParser();
        var binary = new byte[]
        {
            0x34, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21, 0x21, 0x21
        };
        var packets = parser.Parse(binary);

        Assert.Equal(1, packets.Count);
        Assert.Equal(PacketType.Message, packets.First().Type);
        var expectedMessageText = "Hello!!!";
        Assert.Equal(expectedMessageText, packets.First().ToString());
    }


    [Fact]
    public void ShouldParsePacketWithoutPayload()
    {
        var parser = new DefaultPacketParser();
        var binary = new byte[]
        {
            0x32
        };
        var packets = parser.Parse(binary).First();

        Assert.Equal(PacketType.Ping, packets.Type);
    }
}