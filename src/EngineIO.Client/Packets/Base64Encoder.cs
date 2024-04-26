using System;
using System.Text;

namespace EngineIO.Client.Packets;

public class Base64Encoder : IEncoder
{
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data, Encoding encoding)
    {
        var base64 = Convert.ToBase64String(data.Span);
        return new ReadOnlyMemory<byte>(encoding.GetBytes(base64));
    }
}