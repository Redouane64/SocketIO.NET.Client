namespace EngineIO.Client;public interface IPacketParser{    IReadOnlyCollection<Packet> Parse(byte[] binary);    }