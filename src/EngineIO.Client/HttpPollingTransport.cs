using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineIO.Client;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private static readonly int _protocol = 4;
    private static readonly string _transport = "polling";

    private readonly HttpClient _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public HttpPollingTransport(HttpClient client)
    {
        _client = client;
    }

    
    private bool _handshake;
    private string _path = $"/engine.io?EIO={_protocol}&transport={_transport}";

    internal string Path
    {
        get
        {
            if (!_handshake) return _path;
            _path = $"/engine.io?EIO={_protocol}&transport={_transport}&sid={Sid}";
            return _path;
        }
    }
    public int MaxPayload { get; private set; }
    public int PingInterval { get; private set; }
    public int PingTimeout { get; private set; }
    public string? Sid { get; private set; }
    public string[]? Upgrades { get; private set; }
    public string Transport => _transport;

    public async Task<IReadOnlyCollection<byte[]>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_handshake) throw new Exception("Transport is not connected");
        var packets = new Collection<byte[]>();
        
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            var data = await _client.GetByteArrayAsync(Path, cancellationToken);
            var separator = 0x1E;
            var start = 0;
            for (var index = start; index < data.Length; index++)
            {
                if (data[index] == separator)
                {
                    var startIdx = new Index(start);
                    var endIdx = new Index(index - start);
                    var packet = data[startIdx..endIdx];
                    packets.Add(packet);
                    start = index + 1;
                }
            }

            if (start < data.Length)
            {
                var packet = data.AsSpan(start).ToArray();
                packets.Add(packet);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        return packets;
    }

    public async Task SendAsync(byte[] packet,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_handshake) throw new Exception("Transport is not connected");
            await _semaphore.WaitAsync(cancellationToken);

            using var content = new ByteArrayContent(packet);
            using var response = await _client.PostAsync(Path, content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new Exception("Unexpected response from remote server");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async IAsyncEnumerable<byte[]> Poll([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var interval = PingInterval;
        using var packetReaderTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            interval));
        
        while (!cancellationToken.IsCancellationRequested &&
               await packetReaderTimer.WaitForNextTickAsync(cancellationToken))
        {
            var packets = await GetAsync(cancellationToken);
            foreach (var packet in packets) yield return packet;
        }
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake) return;
        var data = await _client.GetByteArrayAsync(Path, cancellationToken);
        var handshakePacket = (PacketType)data[0];

        if (handshakePacket != PacketType.Open)
            throw new Exception("Unexpected packet type");
        var handshake = JsonSerializer
            .Deserialize<HandshakePayload>(data.AsSpan()[1..]);
        if (handshake is null) throw new Exception("Invalid handshake packet");
        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        Upgrades = handshake.Upgrades;
        PingTimeout = handshake.PingTimeout;
        PingInterval = handshake.PingInterval;

        _handshake = true;
        Debug.WriteLine("Handshake completed successfully");
    }
    
    public void Dispose()
    {
        _client.Dispose();
        _semaphore.Dispose();
    }
    
    private class HandshakePayload
    {
        [JsonPropertyName("sid")]
        public required string Sid { get; set; }

        [JsonPropertyName("upgrades")] 
        public required string[] Upgrades { get; set; }

        [JsonPropertyName("pingInterval")] 
        public required int PingInterval { get; set; }

        [JsonPropertyName("pingTimeout")] 
        public required int PingTimeout { get; set; }

        [JsonPropertyName("maxPayload")] 
        public required int MaxPayload { get; set; }
    }
}