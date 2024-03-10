using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using EngineIO.Client.Packets;
using EngineIO.Client.Transport;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly ILogger<Engine> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // storage for streamable messages
    private readonly ConcurrentQueue<Packet> _streamablePackets = new();

    // Client state variables
    private readonly ClientOptions _clientOptions = new();
    
    private bool _connected;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport? _wsTransport;
    private ITransport _currentTransport;
    
    private CancellationTokenSource _pollingCancellationTokenSource = new();

#pragma warning disable CS8618
    public Engine(Action<ClientOptions> configure, ILoggerFactory loggerFactory)
#pragma warning restore CS8618
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Engine>();
        configure(_clientOptions);
    }
    
    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _httpTransport.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        _currentTransport = _httpTransport = new HttpPollingTransport(
            _clientOptions.Uri!, _loggerFactory.CreateLogger<HttpPollingTransport>());
        
        await _httpTransport.Handshake(_pollingCancellationTokenSource.Token);

        if (_clientOptions.AutoUpgrade && _httpTransport.Upgrades!.Contains("websocket"))
        {
            _logger.LogDebug("Upgrading to Websocket transport");
            await Upgrade();
            _pollingCancellationTokenSource = new CancellationTokenSource();
        }

        _connected = true;
        _logger.LogDebug("Client connected");

#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
    }
    
    private void StartPolling()
    {
        StartTransportPolling(_currentTransport).ConfigureAwait(false);
    }

    private async Task StartTransportPolling(ITransport transport)
    {
        await foreach (var packet in transport.PollAsync(_clientOptions.PollingInterval, _pollingCancellationTokenSource.Token))
        {
            if (packet.Type == PacketType.Close)
            {
                _logger.LogDebug("Connection dropped by remote server");
                await DisconnectAsync().ConfigureAwait(false);
                break;
            }

            if (packet.Type == PacketType.Message)
            {
                _streamablePackets.Enqueue(packet);
            }
        }
    }
    
    private async Task Upgrade()
    {
        await _pollingCancellationTokenSource.CancelAsync();
        _pollingCancellationTokenSource.Dispose();

        _currentTransport = _wsTransport =
            new WebSocketTransport(_clientOptions.Uri!, _httpTransport.Sid!,
                _loggerFactory.CreateLogger<WebSocketTransport>());
        
        await _wsTransport.Handshake();
    }

    public async Task DisconnectAsync()
    {
        if (!_connected)
        {
            return;
        }

        await _pollingCancellationTokenSource.CancelAsync();
        await _currentTransport.Disconnect();
        _connected = false;
    }

    public async IAsyncEnumerable<Packet> Stream(TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(interval);

        while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_streamablePackets.TryDequeue(out var packet))
            {
                yield return packet;
            }
        }
    }

    /// <summary>
    /// Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateMessagePacket(text);
        await _currentTransport.SendAsync(packet, cancellationToken);
    }

    /// <summary>
    /// Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(ReadOnlyMemory<byte> binary, CancellationToken cancellationToken = default)
    {
        var packet = Packet.CreateBinaryPacket(binary);
        await _currentTransport.SendAsync(packet, cancellationToken);
    }

}
