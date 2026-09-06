using System.Text;

using EngineIO.Client.Packets;
using EngineIO.Client.Tests.Extensions;

using Moq;

namespace EngineIO.Client.Tests;

public class EngineTests
{
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    async Task Should_Reply_Pong_To_Server_Ping()
    {
        var (engine, requests) = CreateEngine(Handshake(), Packet("2"), Packet("1"));
        await engine.ConnectAsync();

        await Drain(engine);

        Assert.Contains(requests, request =>
            request.Method == HttpMethod.Post && Encoding.UTF8.GetString(request.Body) == "3");
    }

    [Fact]
    async Task Should_Surface_Only_Message_Packets_To_The_Listener()
    {
        var (engine, _) = CreateEngine(Handshake(), Packet("2"), Packet("4Hi"), Packet("1"));
        await engine.ConnectAsync();

        var received = await Drain(engine);

        var packet = Assert.Single(received);
        Assert.Equal(PacketType.Message, packet.Type);
        Assert.Equal("Hi", Encoding.UTF8.GetString(packet.Body.Span));
    }

    [Fact]
    async Task Should_Complete_Listener_When_Server_Sends_Close()
    {
        var (engine, _) = CreateEngine(Handshake(), Packet("1"));
        await engine.ConnectAsync();

        // Completing rather than throwing is the contract: the await foreach ends.
        var received = await Drain(engine);

        Assert.Empty(received);
    }

    [Fact]
    async Task Should_Stop_Polling_After_Server_Close()
    {
        var (engine, requests) = CreateEngine(Handshake(), Packet("1"));
        await engine.ConnectAsync();
        await Drain(engine);

        var pollsAtClose = requests.Count(request => request.Method == HttpMethod.Get);
        await Task.Delay(200);

        Assert.Equal(pollsAtClose, requests.Count(request => request.Method == HttpMethod.Get));
    }

    [Fact]
    async Task Should_Close_Connection_When_No_Ping_Arrives_Within_The_Budget()
    {
        // Budget is pingInterval + pingTimeout = 100ms, and noop is not a ping.
        var (engine, _) = CreateEngine(TimeSpan.FromMilliseconds(20),
            Handshake(pingInterval: 50, pingTimeout: 50), Packet("6"));
        await engine.ConnectAsync();

        await Task.Delay(500);

        // The spec rule is that the client treats the connection as closed. How the
        // listener terminates is deliberately not asserted: a server close completes
        // it, while the watchdog currently cancels it.
        Assert.False(engine.Connected);
    }

    [Fact]
    async Task Should_Not_Close_Connection_While_Pings_Keep_Arriving()
    {
        var (engine, _) = CreateEngine(TimeSpan.FromMilliseconds(20),
            Handshake(pingInterval: 50, pingTimeout: 50), Packet("2"));
        await engine.ConnectAsync();

        // Several budgets' worth of pings must not trip the watchdog.
        await Task.Delay(500);

        Assert.True(engine.Connected);
        await engine.DisconnectAsync();
    }

    [Fact]
    async Task Should_Not_Upgrade_When_Server_Does_Not_Advertise_Websocket()
    {
        var (engine, _) = CreateEngine(autoUpgrade: true, TimeSpan.Zero,
            Handshake(upgrades: ""), Packet("1"));

        await engine.ConnectAsync();

        Assert.Equal("polling", engine.TransportName);
        await Drain(engine);
    }

    [Fact]
    async Task Should_Not_Upgrade_When_AutoUpgrade_Is_Disabled()
    {
        var (engine, _) = CreateEngine(autoUpgrade: false, TimeSpan.Zero,
            Handshake(upgrades: "\"websocket\""), Packet("1"));

        await engine.ConnectAsync();

        Assert.Equal("polling", engine.TransportName);
        await Drain(engine);
    }

    private static async Task<List<Packet>> Drain(Engine engine)
    {
        var received = new List<Packet>();
        using var timeout = new CancellationTokenSource(ListenTimeout);

        await foreach (var packet in engine.ListenAsync(timeout.Token))
        {
            received.Add(packet);
        }

        return received;
    }

    private static (Engine Engine, List<CapturedRequest> Requests) CreateEngine(params byte[][] responses)
    {
        return CreateEngine(TimeSpan.Zero, responses);
    }

    private static (Engine Engine, List<CapturedRequest> Requests) CreateEngine(
        TimeSpan pollDelay, params byte[][] responses)
    {
        return CreateEngine(false, pollDelay, responses);
    }

    private static (Engine Engine, List<CapturedRequest> Requests) CreateEngine(
        bool autoUpgrade, TimeSpan pollDelay, params byte[][] responses)
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var requests = mockHttpMessageHandler.MockPollingServer(pollDelay, responses);
        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("http://foo.bar")
        };

        var engine = new Engine(options =>
        {
            options.BaseAddress = "http://foo.bar";
            options.AutoUpgrade = autoUpgrade;
        }, httpClient);

        return (engine, requests);
    }

    private static byte[] Packet(string payload)
    {
        return Encoding.UTF8.GetBytes(payload);
    }

    private static byte[] Handshake(int pingInterval = 25000, int pingTimeout = 20000,
        string upgrades = "\"websocket\"")
    {
        return Encoding.UTF8.GetBytes(
            $$"""0{"sid":"1NkM2QzZGMjEyMTIxCg","maxPayload":1000000,"pingTimeout":{{pingTimeout}},"pingInterval":{{pingInterval}},"upgrades":[{{upgrades}}]}""");
    }
}