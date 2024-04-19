using System.Net.WebSockets;
using EngineIO.Client.Transports;

namespace EngineIO.Client.Tests.Transports;

public sealed class WebSocketTransportTests
{
    [Fact]
    async void Should_Create_Transport()
    {
        var baseAddress = "http://example.com";
        var sid = "1NkM2QzZGMjEyMTIxCg";
        var client = new ClientWebSocket();
        var transport = new WebSocketTransport(baseAddress, sid, client);
    }
}
