using System.Collections.Concurrent;
using EngineIO.Client.Packets;
using EngineIO.Client.Transport;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    // storage for streamable messages
    private readonly ConcurrentQueue<Packet> _streamablePackets = new();
    private readonly ClientOptions _clientOptions = new();
    
    private bool _connected;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport _wsTransport;
    // reference current active transport
    private ITransport _transport;
    
    private CancellationTokenSource _pollingCancellationTokenSource = new();
    
    public Engine(Action<ClientOptions> configure)
    {
        configure(_clientOptions);
    }
    
    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        _transport = _httpTransport = new HttpPollingTransport(_clientOptions.Uri!);
        await _httpTransport.Handshake(_pollingCancellationTokenSource.Token);

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            await _pollingCancellationTokenSource.CancelAsync();
            _pollingCancellationTokenSource.Dispose();
            _transport = _wsTransport = new WebSocketTransport(_clientOptions.Uri!, _httpTransport.Sid!);
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
            var packets = await _transport.GetAsync(_pollingCancellationTokenSource.Token);
            
            foreach (var packet in packets)
            {
                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
#pragma warning disable CS4014 
                    _transport.SendAsync(Packet.PongPacket, _pollingCancellationTokenSource.Token).ConfigureAwait(false);
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
        await _transport.Disconnect();
        _connected = false;
    }

    /// <summary>
    /// Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateMessagePacket(text);
        await _transport.SendAsync(packet, cancellationToken);
    }

    /// <summary>
    /// Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateBinaryPacket(binary);
        await _transport.SendAsync(packet, cancellationToken);
    }

}
