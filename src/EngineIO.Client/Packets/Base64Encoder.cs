using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace EngineIO.Client.Packets;

public class Base64Encoder : IEncoder
{
    public ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> data, Encoding encoding)
    {
        // Base64 output is ASCII, so under UTF-8 the encoded characters and the
        // transcoded bytes are identical: write them straight out and skip the
        // intermediate string entirely.
        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            var utf8 = new byte[Base64.GetMaxEncodedToUtf8Length(data.Length)];
            Base64.EncodeToUtf8(data.Span, utf8, out _, out var written);
            return new ReadOnlyMemory<byte>(utf8, 0, written);
        }

        var base64 = Convert.ToBase64String(data.Span);
        return new ReadOnlyMemory<byte>(encoding.GetBytes(base64));
    }

    public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data, Encoding encoding)
    {
        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            var bytes = new byte[Base64.GetMaxDecodedFromUtf8Length(data.Length)];
            if (Base64.DecodeFromUtf8(data.Span, bytes, out _, out var written) != OperationStatus.Done)
            {
                throw new FormatException("Payload is not a valid base64 sequence.");
            }

            return new ReadOnlyMemory<byte>(bytes, 0, written);
        }

        var base64 = encoding.GetString(data.Span);
        return new ReadOnlyMemory<byte>(Convert.FromBase64String(base64));
    }
}