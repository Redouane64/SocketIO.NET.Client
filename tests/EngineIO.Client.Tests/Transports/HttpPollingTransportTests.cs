using System.Text;

using EngineIO.Client.Packets;
using EngineIO.Client.Tests.Extensions;
using EngineIO.Client.Transports;
using EngineIO.Client.Transports.Exceptions;

using Moq;

namespace EngineIO.Client.Tests.Transports;

public class HttpPollingTransportTests
{
    [Fact]
    void Should_Create_Transport()
    {
        var transport = new HttpPollingTransport("http://127.0.0.1:3000");
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
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Equal(3, packets.Count);
        Assert.Equal(PacketType.Message, packets[0].Type);
        Assert.Equal("Hello", Encoding.UTF8.GetString(packets[0].Body.Span));

        Assert.Equal(PacketType.Ping, packets[1].Type);
        Assert.Equal(PacketType.Message, packets[2].Type);
        Assert.Equal("World", Encoding.UTF8.GetString(packets[2].Body.Span));

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
        Assert.Equal(PacketType.Message, packets[0].Type);
        Assert.Equal("Hello", Encoding.UTF8.GetString(packets[0].Body.Span));
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

    [Theory(DisplayName = "Separators that enclose no payload are skipped")]
    [InlineData("4Hi\u001e")]
    [InlineData("\u001e4Hi")]
    [InlineData("4Hi\u001e\u001e")]
    async Task Should_Ignore_Empty_Payloads_Between_Separators(string response)
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(Encoding.UTF8.GetBytes(response));
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Single(packets);
        Assert.Equal(PacketType.Message, packets[0].Type);
        Assert.Equal("Hi", Encoding.UTF8.GetString(packets[0].Body.Span));
    }

    [Fact]
    async Task Should_Decode_Base64_Binary_Packet()
    {
        var body = Encoding.UTF8.GetBytes("Hi");
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(Encoding.UTF8.GetBytes($"b{Convert.ToBase64String(body)}"));
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Single(packets);
        Assert.Equal(PacketFormat.Binary, packets[0].Format);
        Assert.Equal(PacketType.Message, packets[0].Type);

        // The body must be the decoded bytes, not the base64 text that carried them.
        Assert.True(packets[0].Body.Span.SequenceEqual(body));
    }

    [Fact]
    async Task Should_Skip_Unparseable_Packet()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(new byte[]
        {
            0x01, (byte)'x', 0x1e, (byte)'4', (byte)'H', (byte)'i'
        });
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var packets = await transport.GetAsync(CancellationToken.None);

        Assert.Single(packets);
        Assert.Equal("Hi", Encoding.UTF8.GetString(packets[0].Body.Span));
    }

    [Fact]
    async Task Should_Reject_Handshake_That_Is_Not_An_Open_Packet()
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.MockGetByteArrayAsync(Encoding.UTF8.GetBytes("4Hello"));
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        var exception = await Assert.ThrowsAsync<TransportException>(
            () => transport.ConnectAsync(CancellationToken.None));

        Assert.Equal(ErrorReason.InvalidPacket, exception.ErrorReason);
    }

    [Fact]
    async Task Should_Send_Sid_On_Every_Request_After_Handshake()
    {
        var sid = "1NkM2QzZGMjEyMTIxCg";
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var requests = mockHttpMessageHandler.MockPollingServer(
            Encoding.UTF8.GetBytes(Handshake(sid)),
            Encoding.UTF8.GetBytes("4Hi"));
        var transport = new HttpPollingTransport(
            new HttpClient(mockHttpMessageHandler.Object) { BaseAddress = new Uri("http://foo.bar") }
        );

        await transport.ConnectAsync(CancellationToken.None);
        await transport.GetAsync(CancellationToken.None);
        await transport.SendAsync(Packet.CreateMessagePacket("Hello"), CancellationToken.None);

        Assert.Equal(3, requests.Count);
        Assert.DoesNotContain("sid=", requests[0].Uri.Query);
        Assert.Contains($"sid={sid}", requests[1].Uri.Query);
        Assert.Contains($"sid={sid}", requests[2].Uri.Query);
    }

    internal static string Handshake(string sid, int maxPayload = 1_000_000)
    {
        return $$"""0{"sid":"{{sid}}","maxPayload":{{maxPayload}},"pingTimeout":20000,"pingInterval":25000,"upgrades":["polling","websocket"]}""";
    }
}