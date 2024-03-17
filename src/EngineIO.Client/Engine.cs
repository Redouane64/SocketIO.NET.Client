using System.Collections.Concurrent;
using System.Net.WebSockets;
using EngineIO.Client.Packets;
using EngineIO.Client.Transport;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ClientWebSocket _wsClient;
    private readonly ClientOptions _clientOptions = new();
    private readonly IEncoder _base64Encoder = new Base64Encoder();

    private readonly string _baseAddress;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport _wsTransport;

    private bool _connected;

    internal CancellationTokenSource PollingCancellationTokenSource = new();

    public Engine(Action<ClientOptions> configure)
    {
        configure(_clientOptions);
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(_clientOptions.Uri);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;

        _wsClient = new ClientWebSocket();
        _baseAddress = _clientOptions.Uri;
    }

    internal ITransport Transport { get; private set; }
    internal ConcurrentQueue<Packet> Messages { get; } = new ConcurrentQueue<Packet>();

    public void Dispose()
    {
        PollingCancellationTokenSource.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        Transport = _httpTransport = new HttpPollingTransport(_httpClient);
        await _httpTransport.ConnectAsync(PollingCancellationTokenSource.Token);

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            await PollingCancellationTokenSource.CancelAsync();
            PollingCancellationTokenSource.Dispose();
            Transport = _wsTransport = new WebSocketTransport(_baseAddress, _httpTransport.Sid!, _wsClient);
            await _wsTransport.ConnectAsync();

            PollingCancellationTokenSource = new CancellationTokenSource();
        }

        _connected = true;
#pragma warning disable CS4014
        Task.Run(() => PollAsync().ConfigureAwait(false), PollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014
    }

    internal async Task PollAsync()
    {
        while (!PollingCancellationTokenSource.IsCancellationRequested)
        {
            var packets = await Transport.GetAsync(PollingCancellationTokenSource.Token);

            foreach (var data in packets)
            {
                var packet = Packet.Parse(data);

                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
#pragma warning disable CS4014 
                    Transport.SendAsync(Packet.PongPacket.ToPlaintextPacket(), PacketFormat.PlainText,
                        PollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014 
                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    await PollingCancellationTokenSource.CancelAsync();
                    // TODO:
                    break;
                }

                if (packet.Type == PacketType.Message)
                {
                    Messages.Enqueue(packet);
                }
            }

        }
    }

    public async Task DisconnectAsync()
    {
        if (!_connected)
        {
            return;
        }

        await PollingCancellationTokenSource.CancelAsync();
        await Transport.Disconnect();
        _connected = false;
    }

    /// <summary>
    /// Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateMessagePacket(text).ToPlaintextPacket();
        await Transport.SendAsync(packet, PacketFormat.PlainText, cancellationToken);
    }

    /// <summary>
    /// Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateBinaryPacket(binary).ToBinaryPacket(_base64Encoder);
        await Transport.SendAsync(packet, PacketFormat.Binary, cancellationToken);
    }
}
