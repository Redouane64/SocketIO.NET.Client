using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client.Transport;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpPollingTransport> _logger;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _pollingCancellationToken = new();
    private bool _handshake;
    private string _path;

    public HttpPollingTransport(string baseAddress, ILogger<HttpPollingTransport> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(baseAddress);
        _path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public HandshakePacket? HandshakePacket { get; set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
    }

    public string Name => "polling";

    public async IAsyncEnumerable<byte[]> PollAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO: maybe interval value should be exposed to consumer.
        var interval = Math.Abs(HandshakePacket!.PingInterval - HandshakePacket.PingTimeout);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            interval));
        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pollingCancellationToken.Token);
        while (!cts.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(cts.Token))
        {
            var packets = await SplitPackets(cts.Token);
            foreach (var packet in packets)
            {
                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet[0] == (byte)PacketType.Ping)
                {
#pragma warning disable CS4014
                    SendHeartbeat(cts.Token);
#pragma warning restore CS4014
                    _logger.LogDebug("Heartbeat completed");
                    continue;
                }

                yield return packet;
            }
        }
    }

    public async Task Disconnect()
    {
        await SendAsync(new[] { (byte)PacketType.Close });
        await _pollingCancellationToken.CancelAsync();
    }

    public async Task<byte[]> GetAsync(CancellationToken cancellationToken = default)
    {
        var data = Array.Empty<byte>();
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            data = await _httpClient.GetByteArrayAsync(_path, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }

        return data;
    }

    public async Task SendAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (!_handshake)
        {
            throw new Exception("Transport is not connected");
        }

        if (packet.Length > HandshakePacket!.MaxPayload)
        {
            throw new Exception("Max packet payload exceeded");
        }

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            using var content = new ByteArrayContent(packet);
            using var response = await _httpClient.PostAsync(_path, content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Unexpected response from remote server");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending packet");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake)
        {
            return;
        }

        var data = await GetAsync(cancellationToken);
        var handshakePacket = (PacketType)data[0];

        if (handshakePacket != PacketType.Open)
        {
            throw new Exception("Unexpected packet type");
        }

        HandshakePacket = JsonSerializer
            .Deserialize<HandshakePacket>(data.AsSpan(1));
        if (HandshakePacket is null)
        {
            throw new Exception("Invalid handshake packet");
        }

        _path += $"&sid={HandshakePacket.Sid}";
        _handshake = true;
        _logger.LogDebug("Handshake completed successfully");
    }

    private void SendHeartbeat(CancellationToken cancellationToken)
    {
#pragma warning disable CS4014
        SendAsync(new[] { (byte)PacketType.Pong }, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS4014
    }

    /// <summary>
    /// Split concatenated packets into a collection of packets.
    /// <a href="https://github.com/socketio/engine.io-protocol?tab=readme-ov-file#http-long-polling-1">Packet encoding</a>
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Packets</returns>
    private async Task<IReadOnlyCollection<byte[]>> SplitPackets(CancellationToken cancellationToken = default)
    {
        var data = await GetAsync(cancellationToken);

        var packets = new Collection<byte[]>();
        var separator = 0x1E;

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == separator)
            {
                var packet = data.AsSpan(start..index).ToArray();
                packets.Add(packet);
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            var packet = data.AsSpan(start).ToArray();
            packets.Add(packet);
        }

        return packets;
    }
}
