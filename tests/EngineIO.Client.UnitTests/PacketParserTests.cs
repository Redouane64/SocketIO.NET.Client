namespace EngineIO.Client.UnitTests;

public class PacketParserTests
{
    [Fact]
    public void ParseShouldReturnPackets()
    {
        var parser = new DefaultPacketParser();
        var binary = new Byte[]
        {
            0x34, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21, 0x21, 0x21, 0x1E, 0x32, 0x1E, 0x34, 0x48, 0x65, 0x6C, 0x6C, 0x6F,
            0x21, 0x21, 0x21
        };

        var packets = parser.Parse(binary);
        
        Assert.Equal(3, packets.Count);
    }
}