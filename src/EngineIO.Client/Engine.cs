using EngineIO.Client.Packets;
using EngineIO.Client.Transport;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly ClientOptions _clientOptions = new();
    private readonly IEncoder base64Encoder = new Base64Encoder();

    private bool _connected;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport _wsTransport;
    private CancellationTokenSource _pollingCancellationTokenSource = new();

    public Engine(Action<ClientOptions> configure)
    {
        configure(_clientOptions);
    }

    public ITransport Transport { get; private set; }

    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        Transport = _httpTransport = new HttpPollingTransport(_clientOptions.Uri!);
        await _httpTransport.Handshake(_pollingCancellationTokenSource.Token);

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            await _pollingCancellationTokenSource.CancelAsync();
            _pollingCancellationTokenSource.Dispose();
            Transport = _wsTransport = new WebSocketTransport(_clientOptions.Uri!, _httpTransport.Sid!);
            await _wsTransport.Handshake();

            _pollingCancellationTokenSource = new CancellationTokenSource();
        }

        _connected = true;
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014
    }

    private void StartPolling()
    {
        Poll().ConfigureAwait(false);
    }

    private async Task Poll()
    {
        while (!_pollingCancellationTokenSource.IsCancellationRequested)
        {
            var packets = await Transport.GetAsync(_pollingCancellationTokenSource.Token);

            foreach (var data in packets)
            {
                var packet = Packet.Parse(data);

                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
#pragma warning disable CS4014 
                    Transport.SendAsync(Packet.PongPacket.ToPlaintextPacket(), PacketFormat.PlainText,
                        _pollingCancellationTokenSource.Token).ConfigureAwait(false);
#pragma warning restore CS4014 
                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    await _pollingCancellationTokenSource.CancelAsync();
                    // TODO:
                    break;
                }

                if (packet.Type == PacketType.Message)
                {
                    // TODO:
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

        await _pollingCancellationTokenSource.CancelAsync();
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
        var packet = Packet.CreateBinaryPacket(binary).ToBinaryPacket(base64Encoder);
        await Transport.SendAsync(packet, PacketFormat.Binary, cancellationToken);
    }
}
