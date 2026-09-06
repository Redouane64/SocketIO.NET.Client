using System.Text;

using EngineIO.Client.Packets;

namespace EngineIO.Client.Tests.Packets;

public class Base64EncoderTests
{
    private readonly IEncoder _encoder = new Base64Encoder();

    [Fact]
    void Encode_Should_Match_Convert_ToBase64String()
    {
        var data = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF };

        var encoded = _encoder.Encode(data, Encoding.UTF8);

        Assert.Equal(Convert.ToBase64String(data), Encoding.UTF8.GetString(encoded.Span));
    }

    [Theory(DisplayName = "Round-trip across the base64 padding boundaries")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10_000)]
    void Decode_Should_Round_Trip_Encode(int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 251);
        }

        var decoded = _encoder.Decode(_encoder.Encode(data, Encoding.UTF8), Encoding.UTF8);

        Assert.True(decoded.Span.SequenceEqual(data));
    }

    [Fact]
    void Decode_Should_Reject_Invalid_Base64()
    {
        Assert.Throws<FormatException>(() => _encoder.Decode(Encoding.UTF8.GetBytes("!!!!"), Encoding.UTF8));
    }
}