using System.Net.WebSockets;
using System.Text;

using EngineIO.Client.Packets;
using EngineIO.Client.Transports;
using EngineIO.Client.Transports.Exceptions;

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
        Assert.Equal("/engine.io/", transport.Uri.AbsolutePath);
        Assert.Equal($"?EIO=4&transport=websocket&sid={sid}", transport.Uri.Query);
    }

    [Theory(DisplayName = "The endpoint is served from the configured path")]
    [InlineData("/socket.io", "/socket.io/")]
    [InlineData("socket.io", "/socket.io/")]
    [InlineData("/socket.io/", "/socket.io/")]
    [InlineData("", "/")]
    void Should_Serve_From_The_Configured_Path(string path, string expectedPath)
    {
        using var transport = new WebSocketTransport("http://example.com", "1NkM2QzZGMjEyMTIxCg", path);

        Assert.Equal(expectedPath, transport.Uri.AbsolutePath);
    }

    [Theory(DisplayName = "A slash on the base address is not doubled by the path")]
    [InlineData("http://example.com", "/socket.io/")]
    [InlineData("http://example.com/", "/socket.io/")]
    [InlineData("http://example.com//", "/socket.io/")]
    void Should_Not_Double_The_Slash_Between_Base_Address_And_Path(string baseAddress, string expectedPath)
    {
        // A server matches on the start of the request path, so "//socket.io/" is a
        // path it does not recognise rather than a tidier spelling of the same one.
        using var transport = new WebSocketTransport(baseAddress, "1NkM2QzZGMjEyMTIxCg", "/socket.io");

        Assert.Equal(expectedPath, transport.Uri.AbsolutePath);
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

    [Fact]
    async Task Should_Send_Binary_As_Unmodified_Binary_Frame()
    {
        var body = new byte[] { 0x00, 0x01, 0xFE, 0xFF };
        var (transport, socket) = ConnectedTransport();
        await transport.ConnectAsync(CancellationToken.None);
        socket.Sent.Clear();

        await transport.SendAsync(Packet.CreateBinaryPacket(body), CancellationToken.None);

        var frame = Assert.Single(socket.Sent);

        // Sent as-is: no 'b' prefix and no base64, which is polling-only framing.
        Assert.Equal(WebSocketMessageType.Binary, frame.Type);
        Assert.Equal(body, frame.Payload);
    }

    [Fact]
    async Task Should_Send_Plaintext_As_Text_Frame_With_Type_Byte()
    {
        var (transport, socket) = ConnectedTransport();
        await transport.ConnectAsync(CancellationToken.None);
        socket.Sent.Clear();

        await transport.SendAsync(Packet.CreateMessagePacket("Hi"), CancellationToken.None);

        var frame = Assert.Single(socket.Sent);
        Assert.Equal(WebSocketMessageType.Text, frame.Type);
        Assert.Equal("4Hi", Encoding.UTF8.GetString(frame.Payload));
    }

    [Fact]
    async Task Should_Reassemble_Message_Spanning_Multiple_Reads()
    {
        var socket = new FakeWebSocket();
        socket.Queue(WebSocketMessageType.Text, Encoding.UTF8.GetBytes("4Hel"), false);
        socket.Queue(WebSocketMessageType.Text, Encoding.UTF8.GetBytes("lo Wo"), false);
        socket.Queue(WebSocketMessageType.Text, Encoding.UTF8.GetBytes("rld"), true);
        using var transport = new WebSocketTransport(socket, "http://example.com", "sid");

        var packets = await transport.GetAsync(CancellationToken.None);

        var packet = Assert.Single(packets);
        Assert.Equal(PacketType.Message, packet.Type);
        Assert.Equal("Hello World", Encoding.UTF8.GetString(packet.Body.Span));
    }

    [Fact]
    async Task Should_Return_Binary_Frame_As_Message_Packet()
    {
        var body = new byte[] { 0x34, 0x00, 0xFF };
        var socket = new FakeWebSocket();
        socket.QueueBinary(body);
        using var transport = new WebSocketTransport(socket, "http://example.com", "sid");

        var packets = await transport.GetAsync(CancellationToken.None);

        var packet = Assert.Single(packets);
        Assert.Equal(PacketFormat.Binary, packet.Format);
        Assert.Equal(PacketType.Message, packet.Type);

        // A binary frame is the payload itself: nothing is stripped off the front,
        // even when the first byte happens to look like a packet type.
        Assert.True(packet.Body.Span.SequenceEqual(body));
    }

    [Fact]
    async Task Should_Return_Only_A_Close_Packet_For_A_Close_Frame()
    {
        var socket = new FakeWebSocket();
        socket.QueueClose();
        using var transport = new WebSocketTransport(socket, "http://example.com", "sid");

        var packets = await transport.GetAsync(CancellationToken.None);

        var packet = Assert.Single(packets);
        Assert.Equal(PacketType.Close, packet.Type);
    }

    [Fact]
    async Task Should_Reject_Pong_Probe_Without_Probe_Payload()
    {
        var socket = new FakeWebSocket();
        socket.QueueText("3");
        using var transport = new WebSocketTransport(socket, "http://example.com", "sid");

        var exception = await Assert.ThrowsAsync<TransportException>(
            () => transport.ConnectAsync(CancellationToken.None));

        Assert.Equal(ErrorReason.InvalidPacket, exception.ErrorReason);
    }

    [Fact]
    async Task Should_Send_Close_Packet_Before_Closing_The_Socket()
    {
        var (transport, socket) = ConnectedTransport();
        await transport.ConnectAsync(CancellationToken.None);
        socket.Sent.Clear();
        socket.Calls.Clear();

        await transport.Disconnect();

        var frame = Assert.Single(socket.Sent);
        Assert.Equal("1", Encoding.UTF8.GetString(frame.Payload));
        Assert.Equal(new[] { "SendAsync", "CloseAsync" }, socket.Calls);
        Assert.DoesNotContain("Abort", socket.Calls);
    }

    [Fact]
    async Task Should_Probe_Then_Upgrade_On_Connect()
    {
        var (transport, socket) = ConnectedTransport();

        await transport.ConnectAsync(CancellationToken.None);

        // ping "probe", read the pong "probe", then commit with an upgrade packet.
        Assert.Equal(new[] { "2probe", "5" }, socket.Sent.Select(f => Encoding.UTF8.GetString(f.Payload)));
        Assert.All(socket.Sent, frame => Assert.Equal(WebSocketMessageType.Text, frame.Type));
        Assert.Equal(new[] { "ConnectAsync", "SendAsync", "ReceiveAsync", "SendAsync" }, socket.Calls);
    }

    [Fact]
    async Task Should_Not_Send_Upgrade_When_The_Probe_Is_Not_Answered()
    {
        var socket = new FakeWebSocket();
        socket.QueueText("3");
        using var transport = new WebSocketTransport(socket, "http://example.com", "sid");

        await Assert.ThrowsAsync<TransportException>(() => transport.ConnectAsync(CancellationToken.None));

        // Only the probe went out: the upgrade must not be committed.
        var frame = Assert.Single(socket.Sent);
        Assert.Equal("2probe", Encoding.UTF8.GetString(frame.Payload));
    }

    [Fact]
    async Task Connected_Should_Be_True_Only_After_The_Upgrade_Completes()
    {
        var (transport, _) = ConnectedTransport();

        Assert.False(transport.Connected);
        await transport.ConnectAsync(CancellationToken.None);

        Assert.True(transport.Connected);
        Assert.True(transport.Connected);
    }

    private static (WebSocketTransport Transport, FakeWebSocket Socket) ConnectedTransport()
    {
        var socket = new FakeWebSocket();

        // The upgrade handshake: the server answers the ping probe with a pong probe.
        socket.QueueText("3probe");
        return (new WebSocketTransport(socket, "http://example.com", "sid"), socket);
    }
}