using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly ILogger<Engine> _logger;
    private readonly HttpPollingTransport _transport;
    private readonly HttpClient _httpClient;
    
    private readonly CancellationTokenSource _pollingCts = new();

    private bool _connected;

    public Engine(string uri, ILogger<Engine> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(uri);
        _transport = new HttpPollingTransport(_httpClient);
    }

    public void Dispose()
    {
        _pollingCts.Dispose();
        _transport.Dispose();
    }

    public async Task ConnectAsync()
    {
        await _transport.Handshake(_pollingCts.Token);
        _connected = true;
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCts.Token).ConfigureAwait(false);
#pragma warning restore CS4014
        _logger.LogDebug("Client connected");
    }

    private async Task StartPolling()
    {
        await foreach (var packet in _transport.Poll(_pollingCts.Token))
        {
            if (packet[0] == (byte)PacketType.Ping)
            {
                _logger.LogDebug($"Heartbeat received");
                Heartbeat();
                continue;
            }

            if (packet[0] == (byte)PacketType.Close)
            {
                _logger.LogDebug("Client dropped by remote server");
                await _pollingCts.CancelAsync();
                _connected = false;
                break;
            }

            // TODO: process non heartbeat packets
            _logger.LogDebug("Non-heartbeat packet skipped");
        }
    }

    private void Heartbeat()
    {
#pragma warning disable CS4014
        _transport.SendAsync(new[] { (byte)PacketType.Pong }, _pollingCts.Token)
            .ContinueWith((_, _) => { _logger.LogDebug($"Heartbeat sent"); }, null,
                TaskContinuationOptions.OnlyOnRanToCompletion)
            .ConfigureAwait(false);
#pragma warning restore CS4014
    }

    public async Task DisconnectAsync()
    {
        if (!_connected) return;

        await _transport.SendAsync(new[] { (byte)PacketType.Close });
        await _pollingCts.CancelAsync();
        _connected = false;
    }
}