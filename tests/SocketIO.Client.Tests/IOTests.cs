using SocketIO.Client.Exceptions;

namespace SocketIO.Client.Tests;

public class IOTests
{
    [Fact]
    async Task Should_Send_Connect_Packet_After_The_Handshake()
    {
        var (io, server) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await io.ConnectAsync();

            Assert.True(io.Connected);
            Assert.Equal("40", Assert.Single(server.Posts));
        }
    }

    [Fact]
    async Task Should_Send_Connect_Packet_For_A_Namespace()
    {
        var (io, server) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await io.ConnectAsync("admin");

            Assert.Equal("40/admin,", Assert.Single(server.Posts));
        }
    }

    [Fact]
    async Task Should_Serve_From_The_Socket_IO_Path()
    {
        var (io, server) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await io.ConnectAsync();

            // The trailing slash is what a Socket.IO server matches on.
            Assert.StartsWith("/socket.io/?EIO=4", server.Requests[0].Uri.PathAndQuery);
        }
    }

    [Fact(DisplayName = "A handshake that never completed is not reported as a connection")]
    async Task ConnectAsync_Should_Throw_When_The_Handshake_Fails()
    {
        // Engine.io answers a handshake with an Open packet; anything else is a
        // protocol error rather than a session.
        var (io, server) = CreateClient(FakePollingServer.Payload("4nonsense"));

        await using (io)
        {
            var exception = await Assert.ThrowsAsync<IOConnectionException>(() => io.ConnectAsync());

            Assert.NotNull(exception.InnerException);
            Assert.False(io.Connected);
            Assert.Empty(server.Posts);
        }
    }

    [Fact(DisplayName = "A failed attempt does not leave the client thinking it is connected")]
    async Task ConnectAsync_Should_Handshake_Again_After_A_Failed_Attempt()
    {
        var (io, server) = CreateClient(FakePollingServer.Payload("4nonsense"), FakePollingServer.Handshake());

        await using (io)
        {
            await Assert.ThrowsAsync<IOConnectionException>(() => io.ConnectAsync());

            await io.ConnectAsync();

            Assert.True(io.Connected);
            Assert.Equal("40", Assert.Single(server.Posts));
        }
    }

    [Fact]
    async Task SendAsync_Should_Throw_When_Not_Connected()
    {
        var (io, _) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await Assert.ThrowsAsync<IOConnectionException>(() => io.SendAsync("Hello!"));
        }
    }

    [Fact]
    async Task Should_Send_A_Text_Event()
    {
        var (io, server) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await io.ConnectAsync();
            await io.SendAsync("Hello!", "greeting");

            Assert.Equal("""42["greeting","Hello!"]""", server.Posts[1]);
        }
    }

    [Fact(DisplayName = "A binary event is a header followed by its attachment")]
    async Task Should_Send_A_Binary_Event_As_Header_Then_Attachment()
    {
        var (io, server) = CreateClient(FakePollingServer.Handshake());

        await using (io)
        {
            await io.ConnectAsync();
            await io.SendAsync(new byte[] { 1, 2, 3 });

            Assert.Equal("""451-["message",{"_placeholder":true,"num":0}]""", server.Posts[1]);

            // Long-polling carries text only, so the attachment travels base64-encoded
            // behind a 'b' prefix.
            Assert.Equal("b" + Convert.ToBase64String([1, 2, 3]), server.Posts[2]);
        }
    }

    private static (IO Client, FakePollingServer Server) CreateClient(params byte[][] pollResponses)
    {
        // No websocket among the advertised upgrades, so the client stays on polling
        // and every packet it sends is a POST this server can be asked about.
        var server = new FakePollingServer(pollResponses);
        var httpClient = new HttpClient(server) { BaseAddress = new Uri("http://foo.bar") };

        return (new IO(httpClient, "http://foo.bar"), server);
    }
}