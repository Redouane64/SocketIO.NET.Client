using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIO.Client;

public sealed class Engine
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<Engine> _logger;
    private readonly HttpPollingTransport _transport;

    private bool _connected;

    public Engine(string uri, ILogger<Engine>? logger)
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(uri);

        _logger = logger ??= NullLogger<Engine>.Instance;

        _transport = new HttpPollingTransport(_httpClient,
            logger);
    }

    public async Task ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _transport.Handshake(cancellationToken);
        _connected = true;
#pragma warning disable CS4014
        Task.Run(GetPackets, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS4014
        _logger.LogDebug("Client connected");
    }

    private async Task GetPackets()
    {
        var interval = Math.Abs(_transport.PingInterval - _transport.PingTimeout);

        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            interval));

        while (await timer.WaitForNextTickAsync() && !_cancellationTokenSource.IsCancellationRequested)
        {
            var packets = await _transport.GetAsync(_cancellationTokenSource.Token);
            foreach (var packet in packets)
            {
                if (packet.Type == PacketType.Ping)
                {
                    Heartbeat();
                    continue;
                }

                if (packet.Type == PacketType.Close)
                {
                    await _cancellationTokenSource.CancelAsync();
                    Console.WriteLine($"[{DateTime.Now}] Client dropped by remote server");
                    _connected = false;
                    return;
                }

                // TODO: process non heartbeat packets
                Console.WriteLine($"[{DateTime.Now}] Non-heartbeat packet skipped");
            }
        }
    }

    private void Heartbeat()
    {
#pragma warning disable CS4014
        _transport.SendAsync(Packet.Pong(), _cancellationTokenSource.Token)
            .ContinueWith((_, _) => { Console.WriteLine($"[{DateTime.Now}] Heartbeat"); }, null,
                TaskContinuationOptions.OnlyOnRanToCompletion)
            .ConfigureAwait(false);
#pragma warning restore CS4014
    }

    public async Task DisconnectAsync()
    {
        await _cancellationTokenSource.CancelAsync();
        _connected = false;
    }
}