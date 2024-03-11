using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIO.Client.Packets;
using Microsoft.Extensions.Logging;

namespace EngineIO.Client.Transport;

public sealed class HttpPollingTransport : ITransport, IDisposable
{
    private readonly HttpClient _httpClient;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _pollingCancellationToken = new();
    
    private bool _handshake;
    private string _path;

    public HttpPollingTransport(string baseAddress)
    {
        _httpClient = new HttpClient();

        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        
        _httpClient.BaseAddress = new Uri(baseAddress);
        _path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public string Name => "polling";
    
    public string? Sid { get; private set; }

    public string[]? Upgrades { get; private set; }

    public int PingInterval { get; private set; }

    public int PingTimeout { get; private set; }

    public int MaxPayload { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
        _pollingCancellationToken.Dispose();
    }
    
    public async IAsyncEnumerable<Packet> PollAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var pollingCancellationToken = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, _pollingCancellationToken.Token);
        while (!pollingCancellationToken.IsCancellationRequested)
        {
            var packets = await GetAsync(cancellationToken);
            foreach (var packet in packets)
            {
                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
#pragma warning disable CS4014 
                    SendAsync(Packet.PongPacket, _pollingCancellationToken.Token).ConfigureAwait(false);
#pragma warning restore CS4014 
                    continue;
                }
                
                if (packet.Type == PacketType.Close)
                {
                    await _pollingCancellationToken.CancelAsync();
                    _handshake = false;
                    break;
                }

                yield return packet;
            }
        }
    }

    public async Task Disconnect()
    {
        await SendAsync(Packet.ClosePacket);
        await _pollingCancellationToken.CancelAsync();
    }

    public async Task<ReadOnlyCollection<Packet>> GetAsync(CancellationToken cancellationToken = default)
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
    
        var packets = new Collection<Packet>();
        short separator = 0x1E;

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == separator)
            {
                var payload = new ReadOnlyMemory<byte>(data, start, index - start);
                packets.Add(Packet.Parse(payload));
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            var payload = new ReadOnlyMemory<byte>(data, start, data.Length - start);
            packets.Add(Packet.Parse(payload));
        }

        return packets.AsReadOnly();
    }

    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        if (!_handshake)
        {
            throw new Exception("Transport is not connected");
        }

        if (packet.Length > MaxPayload)
        {
            throw new Exception("Max packet payload exceeded");
        }

        HttpContent? content = null;
        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            if (packet.Format == PacketFormat.Binary)
            {
                var data = packet.ToBinaryPacket(new Base64Encoder());
                content = new ReadOnlyMemoryContent(data);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                content = new ReadOnlyMemoryContent(packet.ToPlaintextPacket());
                content.Headers.ContentType = new MediaTypeHeaderValue("text/plain", "utf-8");
            }
            
            using var response = await _httpClient.PostAsync(_path, content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Unexpected response from remote server");
            }
        }
        finally
        {
            _semaphore.Release();
            content?.Dispose();
        }
    }

    public async Task Handshake(CancellationToken cancellationToken = default)
    {
        if (_handshake)
        {
            return;
        }

        var data = await GetAsync(cancellationToken);
        var packet = data[0];
        
        if (packet.Type != PacketType.Open)
        {
            throw new Exception("Unexpected packet type");
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(packet.Body.Span)!;

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;
        
        _path += $"&sid={Sid}";
        _handshake = true;
    }
    
    private class HandshakePacket
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
