using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private static readonly int _protocol = 4;
    private static readonly string _transport = "polling";

    private readonly HttpClient _client;
    private readonly ILogger<HttpPollingTransport> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private bool _handshake;
    private bool _paused;
    private string _path = $"/engine.io?EIO={_protocol}&transport={_transport}";

    public HttpPollingTransport(HttpClient client, ILogger<HttpPollingTransport> logger)
    {
        _client = client;
        _logger = logger;
    }

    public int MaxPayload { get; private set; }
    public int PingInterval { get; private set; }
    public int PingTimeout { get; private set; }
    public string? Sid { get; private set; }
    public string[]? Upgrades { get; private set; }
    public string Transport => _transport;

    internal string Path
    {
        get
        {
            if (!_handshake)
            {
                return _path;
            }

            _path = $"/engine.io?EIO={_protocol}&transport={_transport}&sid={Sid}";
            return _path;
        }
    }

    public async IAsyncEnumerable<byte[]> PollAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var interval = Math.Abs(PingInterval - PingTimeout);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            interval));

        while (!cancellationToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(cancellationToken))
        {
            var packets = await GetPackets(cancellationToken);
            foreach (var packet in packets)
            {
                yield return packet;
            }
        }
    }

    public async Task<IReadOnlyCollection<byte[]>> GetPackets(
        CancellationToken cancellationToken = default)
    {
        if (!_handshake)
        {
            throw new Exception("Transport is not connected");
        }

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

    public async Task<byte[]> GetAsync(CancellationToken cancellationToken)
    {
        var data = Array.Empty<byte>();
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            data = await _client.GetByteArrayAsync(Path, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }

        return data;
    }

    public async Task SendAsync(byte[] packet,
        CancellationToken cancellationToken = default)
    {
        if (_paused)
        {
            _logger.LogWarning("Attempt to send data while transport is in paused state");
            return;
        }
        
        if (!_handshake)
        {
            throw new Exception("Transport is not connected");
        }

        if (packet.Length > MaxPayload)
        {
            throw new Exception("Max packet payload exceeded");
        }

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            using var content = new ByteArrayContent(packet);
            using var response = await _client.PostAsync(Path, content,
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

        var handshake = JsonSerializer
            .Deserialize<HandshakePayload>(data.AsSpan()[1..]);
        if (handshake is null)
        {
            throw new Exception("Invalid handshake packet");
        }

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        Upgrades = handshake.Upgrades;
        PingTimeout = handshake.PingTimeout;
        PingInterval = handshake.PingInterval;

        _handshake = true;
        Debug.WriteLine("Handshake completed successfully");

    }

    public void Pause()
    {
        _paused = true;
    }

    public void Dispose()
    {
        _client.Dispose();
        _semaphore.Dispose();
    }

    private class HandshakePayload
    {
        [JsonPropertyName("sid")] public required string Sid { get; set; }

        [JsonPropertyName("upgrades")] public required string[] Upgrades { get; set; }

        [JsonPropertyName("pingInterval")] public required int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")] public required int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")] public required int MaxPayload { get; set; }
    }
}
