namespace EngineIO.Client;

public sealed class DefaultPacketParser : IPacketParser
{
    private readonly byte _separator = 0x1E;

    public IReadOnlyCollection<Packet> Parse(byte[] binary)
    {
        var start = 0;
        var packets = new List<Packet>();
        for (var index = start; index < binary.Length; index++)
            if (binary[index] == _separator)
            {
                var packet =
                    new Packet(binary.AsSpan(start, index - start).ToArray());
                packets.Add(packet);
                start = index + 1;
            }

        if (start < binary.Length)
        {
            var packet = new Packet(binary.AsSpan(start).ToArray());
            packets.Add(packet);
        }

        return packets.AsReadOnly();
    }
}