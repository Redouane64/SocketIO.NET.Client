using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using EngineIO.Client.Packet;
using EngineIO.Client.Transport;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly ILogger<Engine> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // storage for streamable messages
    private readonly ConcurrentQueue<byte[]> _streamablePackets = new();

    private readonly string _uri;

    // Client state variables
    private bool _autoUpgrade = true;
    private bool _connected;
    private HttpPollingTransport _httpTransport;
    private WebSocketTransport? _wsTransport;
    private CancellationTokenSource _pollingCancellationTokenSource = new();

#pragma warning disable CS8618
    public Engine(string uri, ILoggerFactory loggerFactory)
#pragma warning restore CS8618
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Engine>();
        _uri = uri;
    }

    public ITransport Transport { get; private set; }

    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _httpTransport.Dispose();
        _wsTransport?.Dispose();
    }

    public async Task ConnectAsync()
    {
        Transport = _httpTransport = new HttpPollingTransport(_uri, _loggerFactory.CreateLogger<HttpPollingTransport>());
        await _httpTransport.Handshake(_pollingCancellationTokenSource.Token);

        if (_autoUpgrade && _httpTransport.HandshakePacket!.Upgrades.Contains("websocket"))
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
        StartTransportPolling(Transport).ConfigureAwait(false);
    }

    private async Task StartTransportPolling(ITransport transport)
    {
        await foreach (var packet in transport.PollAsync(_pollingCancellationTokenSource.Token))
        {
            if (packet[0] == (byte)PacketType.Close)
            {
                _logger.LogDebug("Client dropped by remote server");
                await _pollingCancellationTokenSource.CancelAsync().ConfigureAwait(false);
                await DisconnectAsync();
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
        await _pollingCancellationTokenSource.CancelAsync();
        _pollingCancellationTokenSource.Dispose();

        Transport = _wsTransport =
            new WebSocketTransport(_uri, _httpTransport.HandshakePacket!,
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
        await Transport.Disconnect();
        _connected = false;
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

    /// <summary>
    /// Send plain text message.
    /// </summary>
    /// <param name="text">Plain text message</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        var packet = Encoding.UTF8.GetBytes(text);
        await Transport.SendAsync(PacketFormat.PlainText, packet, cancellationToken);
    }

    /// <summary>
    /// Send binary message.
    /// </summary>
    /// <param name="binary">Binary data</param>
    /// <param name="cancellationToken"></param>
    public async Task SendAsync(byte[] binary, CancellationToken cancellationToken = default)
    {
        await Transport.SendAsync(PacketFormat.Binary, binary, cancellationToken);
    }

}
