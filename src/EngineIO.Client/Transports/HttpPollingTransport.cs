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
    private readonly ILogger<HttpPollingTransport> _logger;

    private readonly int _protocol = 4;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _pollingCancellationToken = new();
    
    // heartbeat
    private Timer _heartbeatTimer;
    private bool _handshake;
    private string _path;

    public HttpPollingTransport(string baseAddress, ILogger<HttpPollingTransport> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();

        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        
        _httpClient.BaseAddress = new Uri(baseAddress);
        _path = $"/engine.io?EIO={_protocol}&transport={Name}";
    }

    public string Name => "polling";
    
    public string Sid { get; private set; }

    public string[] Upgrades { get; private set; }

    public int PingInterval { get; private set; }

    public int PingTimeout { get; private set; }

    public int MaxPayload { get; private set; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _semaphore.Dispose();
        _pollingCancellationToken.Dispose();
    }
    
    public async IAsyncEnumerable<Packet> PollAsync(int interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        using var pollingCancellationToken = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, _pollingCancellationToken.Token);
        
        while (!pollingCancellationToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(pollingCancellationToken.Token))
        {
            var data = await GetAsync(cancellationToken);
            var packets = SplitPackets(data);
            foreach (var packet in packets)
            {
                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Span[0] == (byte)PacketType.Ping)
                {
                    _logger.LogDebug("Heartbeat received");
                    continue;
                }

                PacketFormat format;
                ReadOnlyMemory<byte> content;
                PacketType type = PacketType.Message;
                if (packet.Span[0] == 98)
                {
                    format = PacketFormat.Binary;
                    // skip 'b' from base64 message
                    content = packet[1..];
                }
                else
                {
                    format = PacketFormat.PlainText;
                    content = packet;
                    type = (PacketType)content.Span[0];
                }
                
                yield return new Packet(format, type, content);
            }
        }
    }
    
    private IReadOnlyCollection<ReadOnlyMemory<byte>> SplitPackets(byte[] data)
    {
        var packets = new Collection<ReadOnlyMemory<byte>>();
        short separator = 0x1E;

        var start = 0;
        for (var index = start; index < data.Length; index++)
        {
            if (data[index] == separator)
            {
                var packet = new ReadOnlyMemory<byte>(data, start, index - start);
                packets.Add(packet);
                start = index + 1;
            }
        }

        if (start < data.Length)
        {
            var packet = new ReadOnlyMemory<byte>(data, start, data.Length - start);
            packets.Add(packet);
        }

        return packets;
    }

    public async Task Disconnect()
    {
        await SendAsync(PacketFormat.PlainText, new[] { (byte)PacketType.Close });
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

    public async Task SendAsync(PacketFormat format, ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken = default)
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

            if (format == PacketFormat.Binary)
            {
                // Binary message format example:
                // bAQIDBA==
                // 
                var builder = new StringBuilder();
                builder.Append('b');
                builder.Append(Convert.ToBase64String(packet.Span));
                
                content = new ReadOnlyMemoryContent(Encoding.UTF8.GetBytes(builder.ToString()));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                content = new ReadOnlyMemoryContent(packet);
                // this optional and not specified by the protocol
                content.Headers.ContentType = new MediaTypeHeaderValue("text/plain", "utf-8");
            }
            
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
        if (data[0] != (byte)PacketType.Open)
        {
            throw new Exception("Unexpected packet type");
        }

        var handshake = JsonSerializer
            .Deserialize<HandshakePacket>(data[1..])!;

        Sid = handshake.Sid;
        MaxPayload = handshake.MaxPayload;
        PingInterval = handshake.PingInterval;
        PingTimeout = handshake.PingTimeout;
        Upgrades = handshake.Upgrades;
        
        _path += $"&sid={Sid}";
        _handshake = true;
        _logger.LogDebug("Handshake completed successfully");

#pragma warning disable CS4014
        HeartbeatAsync(PingInterval).ConfigureAwait(false);
#pragma warning restore CS4014
    }
    
    private async Task HeartbeatAsync(int interval)
    {
        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        while (!_pollingCancellationToken.IsCancellationRequested && await heartbeatTimer.WaitForNextTickAsync(
                   _pollingCancellationToken.Token))
        {
            await SendAsync(PacketFormat.PlainText, new[] { (byte)PacketType.Pong }, CancellationToken.None);
            _logger.LogDebug("Heartbeat sent");
        }
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
