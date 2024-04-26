using System.Text;

using EngineIO.Client.Tests.Extensions;
using EngineIO.Client.Transports;

using Moq;

namespace EngineIO.Client.Tests.Transports;

public class HttpPollingTransportTests
{
    [Fact]
    void Should_Create_Transport()
    {
        var transport = new HttpPollingTransport(new HttpClient());
        Assert.Equal($"/engine.io?EIO=4&transport=polling", transport.Path);
    }

    [Fact]
    async Task Should_Parse_Concatenated_Packets()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(new[] {
            (byte)'4',
            (byte)'H',
            (byte)'e',
            (byte)'l',
            (byte)'l',
            (byte)'o',
            (byte)0x1e,
            (byte)'2',
            (byte)0x1e,
            (byte)'4',
            (byte)'W',
            (byte)'o',
            (byte)'r',
            (byte)'l',
            (byte)'d',
        });
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") });

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Equal(3, packets.Count);
        Assert.Equal('4', (char)packets[0].Span[0]);
        Assert.Equal("Hello", Encoding.UTF8.GetString(packets[0][1..].Span));

        Assert.Equal('2', (char)packets[1].Span[0]);
        Assert.Equal('4', (char)packets[2].Span[0]);
        Assert.Equal("World", Encoding.UTF8.GetString(packets[2][1..].Span));

    }

    [Fact]
    async Task Should_Parse_Single_Packet()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(new[] {
            (byte)'4',
            (byte)'H',
            (byte)'e',
            (byte)'l',
            (byte)'l',
            (byte)'o',
        });
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Single(packets);
        Assert.Equal('4', (char)packets[0].Span[0]);
        Assert.Equal("Hello", Encoding.UTF8.GetString(packets[0][1..].Span));
    }

    [Fact]
    async Task Should_Connect()
    {
        var sid = "1NkM2QzZGMjEyMTIxCg";
        var maxPayload = 120000;
        var pingTimeout = 20000;
        var pingInterval = 25000;
        var upgrades = new[] { "polling", "websocket" };
        var handshakePacket =
            $$"""0{"sid":"{{sid}}","maxPayload":{{maxPayload}},"pingTimeout":{{pingTimeout}},"pingInterval":{{pingInterval}},"upgrades":["{{upgrades[0]}}", "{{upgrades[1]}}"]}""";
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(Encoding.UTF8.GetBytes(handshakePacket));
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        await transport.ConnectAsync(CancellationToken.None);

        Assert.Equal(sid, transport.Sid);
        Assert.Equal(maxPayload, transport.MaxPayload);
        Assert.Equal(pingInterval, transport.PingInterval);
        Assert.Equal(pingTimeout, transport.PingTimeout);
        Assert.Equal(upgrades, transport.Upgrades);
        Assert.Equal($"/engine.io?EIO=4&transport=polling&sid={sid}", transport.Path);
    }
}