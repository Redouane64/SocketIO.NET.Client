using EngineIO.Client.Transports;

namespace EngineIO.Client.Tests.Transports;

public sealed class WebSocketTransportTests
{
    [Theory(DisplayName = "The HTTP scheme is mapped to its WebSocket equivalent")]
    [InlineData("http://example.com", "ws")]
    [InlineData("https://example.com", "wss")]
    void Should_Map_Http_Scheme_To_WebSocket_Scheme(string baseAddress, string expectedScheme)
    {
        var sid = "1NkM2QzZGMjEyMTIxCg";

        using var transport = new WebSocketTransport(baseAddress, sid);

        Assert.Equal(expectedScheme, transport.Uri.Scheme);
        Assert.Equal("/engine.io", transport.Uri.AbsolutePath);
        Assert.Equal($"?EIO=4&transport=websocket&sid={sid}", transport.Uri.Query);
    }

    [Theory(DisplayName = "Required constructor arguments are rejected when missing")]
    [InlineData(null, "1NkM2QzZGMjEyMTIxCg")]
    [InlineData("", "1NkM2QzZGMjEyMTIxCg")]
    [InlineData("http://example.com", null)]
    [InlineData("http://example.com", "")]
    void Should_Reject_Missing_Constructor_Arguments(string? baseAddress, string? sid)
    {
        Assert.Throws<ArgumentException>(() => new WebSocketTransport(baseAddress!, sid!));
    }
}