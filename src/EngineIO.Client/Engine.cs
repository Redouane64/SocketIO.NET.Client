using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Engine> _logger;
    private readonly CancellationTokenSource _pollingCancellationTokenSource = new();
    private readonly HttpPollingTransport _transport;

    private bool _connected;

    public Engine(string uri, ILogger<Engine>? logger = null)
    {
        // TODO: maybe accept HttpClient to be used.
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(uri);

        _logger = logger ??= NullLogger<Engine>.Instance;

        _transport = new HttpPollingTransport(_httpClient,
            logger);
    }

    public void Dispose()
    {
        _pollingCancellationTokenSource.Dispose();
        _transport.Dispose();
    }

    public async Task ConnectAsync()
    {
        await _transport.Handshake(_pollingCancellationTokenSource.Token);
        _connected = true;
#pragma warning disable CS4014
        Task.Run(StartPolling, _pollingCancellationTokenSource.Token);
#pragma warning restore CS4014
        _logger.LogDebug("Client connected");
    }

    private async Task StartPolling()
    {
        await foreach (var packet in _transport.Poll(_pollingCancellationTokenSource.Token))
        {
            if (packet.Type == PacketType.Ping)
            {
                Heartbeat();
                continue;
            }

            if (packet.Type == PacketType.Close)
            {
                Console.WriteLine($"[{DateTime.Now}] Client dropped by remote server");
                await _pollingCancellationTokenSource.CancelAsync();
                _connected = false;
                break;
            }

            // TODO: process non heartbeat packets
            Console.WriteLine($"[{DateTime.Now}] Non-heartbeat packet skipped");
        }
    }

    private void Heartbeat()
    {
#pragma warning disable CS4014
        _transport.SendAsync(Packet.PongPacket(), _pollingCancellationTokenSource.Token)
            .ContinueWith((_, _) => { Console.WriteLine($"[{DateTime.Now}] Heartbeat"); }, null,
                TaskContinuationOptions.OnlyOnRanToCompletion)
            .ConfigureAwait(false);
#pragma warning restore CS4014
    }

    public async Task DisconnectAsync()
    {
        if (!_connected) return;

        await _transport.SendAsync(Packet.ClosePacket());
        await _pollingCancellationTokenSource.CancelAsync();
        _connected = false;
    }
}