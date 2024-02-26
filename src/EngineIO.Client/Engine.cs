using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIO.Client;

public sealed class Engine : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<Engine> _logger;
    private readonly HttpPollingTransport _transport;

    private bool _connected;
    private PeriodicTimer _packetReaderTimer;

    public Engine(string uri, ILogger<Engine>? logger)
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(uri);

        _logger = logger ??= NullLogger<Engine>.Instance;

        _transport = new HttpPollingTransport(_httpClient,
            logger);
    }

    public async Task ConnectAsync()
    {
        await _transport.Handshake(_cancellationTokenSource.Token);
        _connected = true;
#pragma warning disable CS4014
        Task.Run(GetPackets, _cancellationTokenSource.Token);
#pragma warning restore CS4014
        _logger.LogDebug("Client connected");
    }

    private async Task GetPackets()
    {
        var interval = Math.Abs(_transport.PingInterval - _transport.PingTimeout);

        _packetReaderTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            interval));

        while (!_cancellationTokenSource.IsCancellationRequested && await _packetReaderTimer.WaitForNextTickAsync())
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
                    Console.WriteLine($"[{DateTime.Now}] Client dropped by remote server");
                    await _cancellationTokenSource.CancelAsync();
                    _connected = false;
                    break;
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
        if (!_connected) return;
        
        await _transport.SendAsync(Packet.ClosePacket());
        await _cancellationTokenSource.CancelAsync();
        _connected = false;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        _httpClient.Dispose();
        _packetReaderTimer.Dispose();
    }
}