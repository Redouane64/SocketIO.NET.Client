using System.Text;

namespace EngineIO.Client.Packets;

public interface IEncoder
{
    ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data, Encoding encoding);
}
