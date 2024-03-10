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

    private readonly ManualResetEventSlim _heartbeatSync = new ManualResetEventSlim(false);
    private Timer _heartbeatTimeoutTimer;
    private bool _hasPingPonged = false;
    
    private bool _handshake;
    private string _path;

    public HttpPollingTransport(string baseAddress, ILogger<HttpPollingTransport> logger)
    {
        _logger = logger;
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
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PingInterval));
        using var pollingCancellationToken = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, _pollingCancellationToken.Token);
        
        while (!pollingCancellationToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(pollingCancellationToken.Token))
        {
            var packets = await GetAsync(cancellationToken);
            foreach (var packet in packets)
            {
                // Handle heartbeat packet and yield the other packet types to the caller
                if (packet.Type == PacketType.Ping)
                {
                    // set current heartbeat as incomplete
                    _hasPingPonged = false;
                    _heartbeatSync.Set();
                    continue;
                }
                
                if (packet.Type == PacketType.Close)
                {
                    _logger.LogDebug("Connection dropped by remote server");
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
                var packetBodyBase64 = Convert.ToBase64String(packet.Body.Span);
                var builder = new StringBuilder(packetBodyBase64.Length + 1);
                builder.Append('b');
                builder.Append(packetBodyBase64);
                
                content = new ReadOnlyMemoryContent(Encoding.UTF8.GetBytes(builder.ToString()));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                content = new ReadOnlyMemoryContent(packet.ToPayload());
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
        _logger.LogDebug("Handshake completed successfully");

        // start heartbeat mechanism
#pragma warning disable CS4014
        Heartbeat(PingInterval).ConfigureAwait(false);
#pragma warning restore CS4014

        var timeout = TimeSpan.FromMilliseconds(PingInterval + PingTimeout);
        _heartbeatTimeoutTimer = new Timer(HeartbeatTimeoutChecker, null, timeout, timeout);
    }

    private void HeartbeatTimeoutChecker(object? state)
    {
        if (!_hasPingPonged)
        {
            Disconnect().ConfigureAwait(false);
        }
    }

    private async Task Heartbeat(int interval)
    {
        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        while (!_pollingCancellationToken.IsCancellationRequested && await heartbeatTimer.WaitForNextTickAsync(
                   _pollingCancellationToken.Token))
        {
            _heartbeatSync.Wait();
            await SendAsync(Packet.PongPacket, _pollingCancellationToken.Token);
            _heartbeatSync.Reset();
            // set current heartbeat as complete
            _hasPingPonged = true;
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
