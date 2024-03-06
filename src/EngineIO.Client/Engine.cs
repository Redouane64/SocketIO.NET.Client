using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<Engine> _logger;
    private readonly CancellationTokenSource _pollingCts = new();

    private readonly string _uri;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport _wsTransport;
    
    // Client state variables
    private bool _connected;
    
    // storage for streamable messages
    private readonly ConcurrentQueue<byte[]> _streamablePackets = new();

#pragma warning disable CS8618
    public Engine(string uri, ILoggerFactory loggerFactory)
#pragma warning restore CS8618
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Engine>();
        _uri = uri;
    }

    public void Dispose()
    {
        _pollingCts.Dispose();
        _httpTransport?.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        _httpTransport = new HttpPollingTransport(_uri, _loggerFactory.CreateLogger<HttpPollingTransport>());
        await _httpTransport.Handshake(_pollingCts.Token);
        
        if (_httpTransport.Upgrades!.Contains(_wsTransport.Transport))
        {
            _logger.LogDebug("Upgrading to Websocket transport");
            await Upgrade();
        }
        
        _connected = true;
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCts.Token).ConfigureAwait(false);
#pragma warning restore CS4014
        _logger.LogDebug("Client connected");
    }
    
    public async IAsyncEnumerable<byte[]> Stream(TimeSpan interval, 
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

    public async Task DisconnectAsync()
    {
        if (!_connected)
        {
            return;
        }

        await _httpTransport.SendAsync(new[] { (byte)PacketType.Close });
        await _pollingCts.CancelAsync();
        _connected = false;
    }

    private async Task StartPolling()
    {
        await foreach (var packet in _httpTransport.PollAsync(_pollingCts.Token).ConfigureAwait(false))
        {
            if (packet[0] == (byte)PacketType.Ping)
            {
                _logger.LogDebug("Heartbeat received");
#pragma warning disable CS4014
                _httpTransport.Heartbeat(_pollingCts.Token).ConfigureAwait(false);
#pragma warning restore CS4014
                continue;
            }

            if (packet[0] == (byte)PacketType.Close)
            {
                _logger.LogDebug("Client dropped by remote server");
                await _pollingCts.CancelAsync().ConfigureAwait(false);
                _connected = false;
                break;
            }

            if (packet[0] == (byte)PacketType.Message)
            {
                // enqueue message payload
                _streamablePackets.Enqueue(packet.AsSpan(1).ToArray());
            }
        }
    }

    private async Task Upgrade()
    {
        // TODO: complete any remaining packets from polling transport
        
        await _pollingCts.CancelAsync();

        _wsTransport = new WebSocketTransport(_uri, _httpTransport.Sid, _loggerFactory.CreateLogger<WebSocketTransport>());
        await _wsTransport.Handshake();

        _pollingCts.TryReset();
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCts.Token).ConfigureAwait(false);
#pragma warning restore CS4014
    }
}
