using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Engine> _logger;

    private readonly CancellationTokenSource _pollingCts = new();
    private readonly CancellationTokenSource _connectionRetryCts = new();
    
    private readonly HttpPollingTransport _httpTransport;
    private readonly Uri _baseHttpUrl;
    
    private WebSocketTransport _wsTransport;
    private readonly Uri _baseWsUrl;
    
    // Client state variables
    private bool _connected;
    
    // storage for streamable messages
    private readonly ConcurrentQueue<byte[]> _streamablePackets = new();

    public Engine(string uri, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Engine>();

        _baseHttpUrl = new Uri(uri);

        var uriBuilder = new StringBuilder();
        var wsUriSchema = _baseHttpUrl.Scheme == Uri.UriSchemeHttps ? Uri.UriSchemeWss : Uri.UriSchemeWs;
        uriBuilder.Append(wsUriSchema);
        uriBuilder.Append("://");
        uriBuilder.Append(_baseHttpUrl.Host);
        if (!_baseHttpUrl.IsDefaultPort)
        {
            uriBuilder.Append($":{_baseHttpUrl.Port}");
        }

        _baseWsUrl = new Uri(uriBuilder.ToString());
        
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(uri);
        _httpTransport = new HttpPollingTransport(_httpClient, loggerFactory.CreateLogger<HttpPollingTransport>());
    }

    public void Dispose()
    {
        _pollingCts.Dispose();
        _httpTransport.Dispose();
    }

    public async Task ConnectAsync()
    {
        await _httpTransport.Handshake(_pollingCts.Token);
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
                Heartbeat().ConfigureAwait(false);
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

    private Task Heartbeat()
    {
#pragma warning disable CS4014
        return _httpTransport.SendAsync(new[] { (byte)PacketType.Pong }, _pollingCts.Token)
            .ContinueWith((_, _) => { _logger.LogDebug("Heartbeat sent"); }, null,
                TaskContinuationOptions.OnlyOnRanToCompletion);
#pragma warning restore CS4014
    }

    public async Task Upgrade()
    {
        _httpTransport.Pause();
        await _pollingCts.CancelAsync();

        var path = _httpTransport.Path.Replace("transport=polling", "transport=websocket");
        _wsTransport = new WebSocketTransport($"{_baseWsUrl.ToString()}{path}");
        await _wsTransport.Handshake();

        _logger.LogDebug("Connection upgraded to websocket transport");
    }
}
